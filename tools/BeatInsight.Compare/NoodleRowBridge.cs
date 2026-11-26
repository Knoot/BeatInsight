using System.Reflection;
using System.Runtime.Loader;

namespace BeatInsight.Compare;

// CLI-only forwarding. Reentry bypasses the bridge while the emitted native body runs.
public static class NoodleRowBridge
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    [ThreadStatic]static int allDepth;
    [ThreadStatic]static int colorDepth;
    public static bool Enabled;
    public static bool CombinedMapping,MappingFirst=true,MappingPostfixFirst=true;
    public static string? CombinedReverseOrderError;
    static MethodInfo? mappingPostfix;
    static System.Reflection.Emit.DynamicMethod? mappingFirstBody,noodleFirstBody;
    public static int AllCalls,ColorCalls,InstalledProcessors;
    static Type processorType=null!;
    static MethodInfo allPostfix=null!,colorPrefix=null!,colorOriginal=null!;
    static System.Reflection.Emit.DynamicMethod allBody=null!;
    static FieldInfo lines=null!,processorField=null!;
    public static void Initialize()
    {
        AllCalls=ColorCalls=InstalledProcessors=allDepth=colorDepth=0;
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        processorType=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.NoodleBeatmapObjectsInTimeRowProcessor",true)!;
        var patches=noodle.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.ProcessNotesNoodleDataInTimeRow",true)!;
        var original=typeof(NoteData).Assembly.GetType("BeatmapObjectsInTimeRowProcessor",true)!;
        lines=original.GetField("_numberOfLines",Flags)!;
        processorField=typeof(BeatmapData).GetField("_beatmapObjectsInTimeRowProcessor",Flags)!;
        allPostfix=patches.GetMethod("ProcessAllNotesInTimeRowPatch",Flags)!;
        colorPrefix=patches.GetMethod("ProcessColorNotesInTimeRowPatch",Flags)!;
        colorOriginal=original.GetMethod("HandleCurrentTimeSliceColorNotesDidFinishTimeSlice",Flags)!;
        allBody=InstalledTranspilerCompiler.Compile(original.GetMethod("HandleCurrentTimeSliceAllNotesAndSlidersDidFinishTimeSlice",Flags)!,
            patches.GetMethod("ProcessColorNotesInTimeRowTranspiler",Flags)!,exceptionBlocks:true).Method;
    }
    public static void InitializeCombined(string gameRoot)
    {
        if(Comparison.HashFile(Path.Combine(gameRoot,"Plugins/MappingExtensions.dll"))!="1EC0B4EDEEF56C721CD09954D4F251F69A62E6459550615142FAEB6856DA0E4F")throw new InvalidDataException("Mapping row assembly changed");
        var mapping=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot,"Plugins/MappingExtensions.dll"));
        var patch=mapping.GetType("MappingExtensions.HarmonyPatches.BeatmapObjectsInTimeRowProcessorHandleCurrentTimeSliceAllNotesAndSlidersDidFinishTimeSlicePatch",true)!;
        mappingPostfix=patch.GetMethod("Postfix",Flags)!;
        var mappingTranspiler=patch.GetMethod("Transpiler",Flags)!;
        var noodle=processorType.Assembly.GetType("NoodleExtensions.HarmonyPatches.ObjectProcessing.ProcessNotesNoodleDataInTimeRow",true)!.GetMethod("ProcessColorNotesInTimeRowTranspiler",Flags)!;
        var original=typeof(NoteData).Assembly.GetType("BeatmapObjectsInTimeRowProcessor",true)!.GetMethod("HandleCurrentTimeSliceAllNotesAndSlidersDidFinishTimeSlice",Flags)!;
        mappingFirstBody=InstalledTranspilerCompiler.Compile(original,mappingTranspiler,exceptionBlocks:true,followingPatches:new[]{noodle}).Method;
        CombinedReverseOrderError=null;
        try{noodleFirstBody=InstalledTranspilerCompiler.Compile(original,noodle,exceptionBlocks:true,followingPatches:new[]{mappingTranspiler}).Method;}
        catch(TargetInvocationException e)when(e.GetBaseException() is InvalidOperationException invalid&&invalid.Message.StartsWith("Cannot find ",StringComparison.Ordinal))
        {noodleFirstBody=null;CombinedReverseOrderError=e.GetBaseException().Message;}
    }
    public static void Install(object map,int numberOfLines)
    {
        if(!Enabled)return;
        if(processorType.IsInstanceOfType(processorField.GetValue(map)))return;
        var processor=Activator.CreateInstance(processorType,Flags,null,new[]{(object)numberOfLines,map},null)!;
        processorField.SetValue(map,processor);InstalledProcessors++;
    }
    public static bool Process(object instance,object slice,float time,bool all)
    {
        if(!Enabled||(all?allDepth:colorDepth)!=0||!processorType.IsInstanceOfType(instance))return false;
        if(all)allDepth++;else colorDepth++;
        try
        {
            int numberOfLines=(int)lines.GetValue(instance)!;
            if(all)
            {
                (CombinedMapping?(MappingFirst?mappingFirstBody!:noodleFirstBody!):allBody).Invoke(null,new[]{instance,slice,(object)time});
                if(CombinedMapping&&MappingPostfixFirst)mappingPostfix!.Invoke(null,new[]{slice});
                allPostfix.Invoke(null,new[]{instance,(object)numberOfLines,slice});
                if(CombinedMapping&&!MappingPostfixFirst)mappingPostfix!.Invoke(null,new[]{slice});
                AllCalls++;
            }
            else
            {
                colorPrefix.Invoke(null,new[]{instance,(object)numberOfLines,slice});
                colorOriginal.Invoke(instance,new[]{slice,(object)time});ColorCalls++;
            }
            return true;
        }
        finally{if(all)allDepth--;else colorDepth--;}
    }
}
