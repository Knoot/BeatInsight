using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BeatInsight.Compare;

public class ControlledRenderingApplicatorProxy : DispatchProxy
{
    public Action<object?[]?> Callback=null!;
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        if(method?.Name!="Apply")throw new NotSupportedException("Unexpected rendering applicator call");
        Callback(args);return null;
    }
}

static class NoodleEarlyRoomChecks
{
    public static object Run(string gameRoot)
    {
        string path=Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll");
        if(Comparison.HashFile(path)!="A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")throw new InvalidDataException("Reinspect changed early room settings callers");
        using var module=ModuleDefinition.ReadModule(path);
        IEnumerable<TypeDefinition> All(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(All));
        bool Calls(MethodDefinition method,string type,string name)=>method.Body.Instructions.Any(i=>(i.OpCode.Code==Code.Call||i.OpCode.Code==Code.Callvirt)
            &&i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name);
        MethodDefinition Method(string type,string name)=>module.Types.Single(t=>t.Name==type).Methods.Single(m=>m.Name==name);
        if(!Calls(Method("VRRenderingParamsSetup","OnEnable"),"IRenderingParamsApplicator","Apply")
            ||!Calls(Method("RenderingParamsSetupApplicator","Apply"),"RenderingParamsSetupApplicator","ApplyMainSettings")
            ||!Calls(Method("RenderingParamsSetupApplicator","ApplyMainSettings"),"SettingsApplicatorSO","ApplyGameSettings")
            ||!Calls(Method("SettingsApplicatorSO","ApplyGameSettings"),"SettingsApplicatorSO","NotifyRoomTransformOffsetWasUpdated"))throw new InvalidDataException("Early room notification path changed");
        var callers=module.Types.SelectMany(All).SelectMany(t=>t.Methods).Where(m=>m.HasBody&&Calls(m,"SettingsApplicatorSO","NotifyRoomTransformOffsetWasUpdated")).Select(m=>m.FullName).Order(StringComparer.Ordinal).ToArray();
        var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var receiverType=assembly.GetType("VRRenderingParamsSetup",true)!;var settingsType=assembly.GetType("SettingsApplicatorSO",true)!;
        var applicatorField=receiverType.GetField("_applicator",flags)!;var sceneField=receiverType.GetField("_sceneType",flags)!;
        var enable=receiverType.GetMethod("OnEnable",flags)!;var notify=settingsType.GetMethod("NotifyRoomTransformOffsetWasUpdated",flags)!;
        var roomEvent=settingsType.GetEvent("roomTransformOffsetDidUpdateEvent",flags)!;
        int cases=0,failed=0;var failures=new List<object>();
        foreach(var sceneType in Enum.GetValues(sceneField.FieldType).Cast<object>().Take(3))foreach(int subscribers in new[]{0,1,3})
        foreach(bool present in new[]{false,true})foreach(bool repeat in new[]{false,true})
        {
            var renderer=RuntimeHelpers.GetUninitializedObject(receiverType);sceneField.SetValue(renderer,sceneType);
            var settings=RuntimeHelpers.GetUninitializedObject(settingsType);int notifications=0,applications=0;bool inCallback=false,synchronous=true;
            var handlers=new List<Action>();
            for(int i=0;i<subscribers;i++){Action handler=()=>{notifications++;synchronous&=inCallback;};handlers.Add(handler);roomEvent.AddEventHandler(settings,handler);}
            if(present)
            {
                var proxy=DispatchProxy.Create(applicatorField.FieldType,typeof(ControlledRenderingApplicatorProxy));
                ((ControlledRenderingApplicatorProxy)proxy).Callback=args=>
                {
                    if(args==null||args.Length!=2||!Equals(args[0],sceneType)||args[1]!=null)throw new InvalidDataException("Unexpected scene setup arguments");
                    applications++;inCallback=true;notify.Invoke(settings,null);inCallback=false;
                };
                applicatorField.SetValue(renderer,proxy);
            }
            enable.Invoke(renderer,null);if(repeat)enable.Invoke(renderer,null);
            int calls=present?(repeat?2:1):0;bool pass=applications==calls&&notifications==calls*subscribers&&synchronous;
            foreach(var handler in handlers)roomEvent.RemoveEventHandler(settings,handler);
            int before=notifications;notify.Invoke(settings,null);pass&=notifications==before;
            cases++;if(!pass){failed++;failures.Add(new{scene=sceneType.ToString(),subscribers,present,repeat,applications,notifications});}
        }
        return new{schemaVersion=1,cases,failed,failures,mainNotificationCallers=callers,gameStarted=false,settingsRead=false,settingsWritten=false,menuAdmissionChanged=false,
            scope="actual VRRenderingParamsSetup.OnEnable with controlled IRenderingParamsApplicator and null scene setup data; actual SettingsApplicatorSO event add/notify/remove; Main-only direct caller and ApplyGameSettings path audit; substituted Apply body skips audio/haptics/controller refresh; actual Unity OnEnable/Start ordering and room Transform writes not executed"};
    }
}
