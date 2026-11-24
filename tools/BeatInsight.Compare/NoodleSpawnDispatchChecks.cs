using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

public class ControlledObjectSpawnerProxy:DispatchProxy
{
    public static bool RecordPrefix(bool fallback)=>NoodleSpawnDispatchChecks.TrackPrefix(fallback);
    internal readonly List<(string Method,object Item,object Spawn)> Calls=new();
    protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)
    {
        if(targetMethod==null||args==null||targetMethod.Name is not ("ProcessNoteData" or "ProcessObstacleData" or "ProcessSliderData"))
            throw new InvalidDataException("Unexpected controlled spawner call");
        if(targetMethod.Name=="ProcessNoteData"?(args.Length!=3||!Equals(args[2],false)):args.Length!=2)
            throw new InvalidDataException("Unexpected controlled spawner arguments");
        Calls.Add((targetMethod.Name,args[0]!,args[1]!));return null;
    }
}

// Only the final IBeatmapObjectSpawner receiver is controlled; native handler IL is executed.
sealed class NoodleSpawnDispatchChecks:IDisposable
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static NoodleSpawnDispatchChecks? current;
    readonly object controller,movement,spawn;
    readonly ControlledObjectSpawnerProxy proxy;
    readonly Dictionary<Type,(DynamicMethod Handler,MethodInfo Geometry,string Process)> handlers=new();
    readonly List<(object Wrapper,FieldInfo Field,Delegate Original)> bindings=new();
    readonly JObject lifetime=new();
    int verifiedPrefix,verifiedFallback;
    int failed,calls,prefixCalls,fallbackCalls,geometryChecks,disabledChecks;
    internal NoodleSpawnDispatchChecks(Assembly noodle,object spawn,object originalController)
    {
        if(current!=null)throw new InvalidOperationException("Nested spawn dispatch context");
        this.spawn=spawn;
        var controllerType=originalController.GetType();
        movement=controllerType.GetField("_beatmapObjectSpawnMovementData",flags)!.GetValue(originalController)!;
        controller=RuntimeHelpers.GetUninitializedObject(controllerType);
        controllerType.GetField("_beatmapObjectSpawnMovementData",flags)!.SetValue(controller,movement);
        var spawnerField=controllerType.GetField("_beatmapObjectSpawner",flags)!;
        var receiver=DispatchProxy.Create(spawnerField.FieldType,typeof(ControlledObjectSpawnerProxy));proxy=(ControlledObjectSpawnerProxy)receiver;
        spawnerField.SetValue(controller,receiver);
        var patchType=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.NoodledSpawnMovementData",true)!;
        var patch=patchType.GetConstructors(flags).Single(c=>!c.IsStatic).Invoke(new[]{spawn});
        var affinity=AffinitySpawnPatchSession.Current??throw new InvalidOperationException("Missing Affinity spawn session");
        affinity.Bind(patch);
        foreach(var spec in new[]{(typeof(NoteData),"HandleNoteDataCallback","GetJumpingNoteSpawnData","NoteNoodlePatch","ProcessNoteData"),
            (typeof(ObstacleData),"HandleObstacleDataCallback","GetObstacleSpawnData","ObstacleNoodlePatch","ProcessObstacleData"),
            (typeof(SliderData),"HandleSliderDataCallback","GetSliderSpawnData","SliderNoodlePatch","ProcessSliderData")})
        {
            var original=movement.GetType().GetMethod(spec.Item3,flags)!;
            var prefix=patchType.GetMethod(spec.Item4,flags)!;
            var bridge=affinity.Geometry(prefix.Name);
            int sites=0;
            var handler=InstalledTranspilerCompiler.Compile(controllerType.GetMethod(spec.Item2,flags)!,typeof(HeckMapChecks).GetMethod(nameof(HeckMapChecks.Identity))!,(code,operand)=>
            {
                if(operand is MethodInfo method&&method==original){sites++;return(OpCodes.Call,bridge);}
                return(code,operand);
            });
            if(sites!=1)throw new InvalidDataException("Spawn handler geometry call changed");
            handlers.Add(spec.Item1,(handler.Method,original,spec.Item5));
        }
        current=this;
    }
    public static bool TrackPrefix(bool fallback)
    {
        var owner=current??throw new InvalidOperationException("Missing spawn dispatch context");
        owner.prefixCalls++;if(fallback)owner.fallbackCalls++;return fallback;
    }
    internal void Attach(object originalController)
    {
        if(bindings.Count!=0)throw new InvalidOperationException("Spawn callbacks already rebound");
        foreach(var spec in new[]{("_noteDataCallbackWrapper",typeof(NoteData)),("_obstacleDataCallbackWrapper",typeof(ObstacleData)),("_sliderDataCallbackWrapper",typeof(SliderData))})
        {
            var wrapper=originalController.GetType().GetField(spec.Item1,flags)!.GetValue(originalController)!;
            var field=wrapper.GetType().GetField("_callback",flags)!;
            var original=(Delegate)field.GetValue(wrapper)!;
            bindings.Add((wrapper,field,original));
            var callback=handlers[spec.Item2].Handler.CreateDelegate(field.FieldType,controller);
            field.SetValue(wrapper,callback);
            if(!ReferenceEquals(field.GetValue(wrapper),callback)||!ReferenceEquals(callback.Target,controller))
                throw new InvalidDataException("Compiled spawn callback binding failed");
        }
        lifetime["boundCallbacks"]=bindings.Count;
    }
    public void Capture(BeatmapObjectData item)
    {
        var type=item switch {NoteData=>typeof(NoteData),ObstacleData=>typeof(ObstacleData),SliderData=>typeof(SliderData),_=>throw new InvalidDataException("Unsupported spawn dispatch type")};
        handlers[type].Handler.Invoke(null,new[]{controller,item});Verify(item);
    }
    public void Verify(BeatmapObjectData item)
    {
        var type=item switch {NoteData=>typeof(NoteData),ObstacleData=>typeof(ObstacleData),SliderData=>typeof(SliderData),_=>throw new InvalidDataException("Unsupported spawn verification type")};
        var spec=handlers[type];calls++;
        if(proxy.Calls.Count!=1||prefixCalls!=verifiedPrefix+1)
        {
            failed++;proxy.Calls.Clear();verifiedPrefix=prefixCalls;verifiedFallback=fallbackCalls;return;
        }
        var actual=proxy.Calls[0];
        if(actual.Method!=spec.Process||!ReferenceEquals(item,actual.Item))failed++;
        object?[] args={item,null};
        bool fallback=(bool)spawn.GetType().GetMethod(spec.Geometry.Name,flags)!.Invoke(spawn,args)!;
        if(fallbackCalls-verifiedFallback!=(fallback?1:0))failed++;
        var expected=fallback?spec.Geometry.Invoke(movement,new object[]{item}):args[1];
        // This compares transport of native geometry, not an independent geometry oracle.
        if(!Equals(actual.Spawn,expected))failed++;geometryChecks++;proxy.Calls.Clear();
        verifiedPrefix=prefixCalls;verifiedFallback=fallbackCalls;
    }
    internal void CheckDisabled(BeatmapObjectData item)
    {
        var field=controller.GetType().GetField("_disableSpawning",flags)!;
        var type=item switch {NoteData=>typeof(NoteData),ObstacleData=>typeof(ObstacleData),SliderData=>typeof(SliderData),_=>throw new InvalidDataException("Unsupported disabled-spawn type")};
        int before=proxy.Calls.Count,beforePrefix=prefixCalls;field.SetValue(controller,true);
        try
        {
            handlers[type].Handler.Invoke(null,new[]{controller,item});
            if(proxy.Calls.Count!=before||prefixCalls!=beforePrefix)failed++;disabledChecks++;
        }
        finally{field.SetValue(controller,false);proxy.Calls.Clear();}
    }
    internal object Evidence()=>new{failed,calls,prefixCalls,fallbackCalls,geometryChecks,disabledChecks,lifetime,
        scope="Actual spawn handler IL with one geometry call redirected to installed HarmonyManipulator composition of original method and installed Affinity-generated Noodle prefix wrapper. Existing native callbacks rebound to closed compiled-handler delegates; queue invokes handlers directly, observer verifies payload. Controlled IBeatmapObjectSpawner; geometry transport comparison, not independent gold. No detours, Unity object creation, transform writes or all-mod Harmony composition."};
    public void Dispose()
    {
        int restored=0;
        try
        {
            foreach(var binding in bindings)
            {
                binding.Field.SetValue(binding.Wrapper,binding.Original);
                if(!ReferenceEquals(binding.Field.GetValue(binding.Wrapper),binding.Original))throw new InvalidDataException("Original spawn callback was not restored");
                restored++;
            }
        }
        finally
        {
            bindings.Clear();if(current==this)current=null;AffinitySpawnPatchSession.Current?.Unbind();proxy.Calls.Clear();handlers.Clear();
            lifetime["restoredCallbacks"]=restored;lifetime["retainedCallbacks"]=bindings.Count;lifetime["disposed"]=true;
        }
    }
}
