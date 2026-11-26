using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace BeatInsight.Compare;

static class NoteDiagnosticCostChecks
{
    const BindingFlags flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    internal static object Run(string gameRoot,string pluginPath)
    {
        var plugin=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(pluginPath));
        var diagnostics=plugin.GetType("BeatInsight.Mod.RuntimeDiagnostics",true)!;
        var capture=plugin.GetType("BeatInsight.Mod.RuntimeCapture",true)!;
        var configuration=plugin.GetType("BeatInsight.Mod.Plugin",true)!.GetProperty("Config",flags)!.GetValue(null)!;
        var diagnosticSetting=configuration.GetType().GetProperty("DiagnosticLogging",flags)!;
        var fields=new[]{"active","noteSamples","notePoseSamples","mainThread","emitted"}.Select(n=>diagnostics.GetField(n,flags)!).ToArray();
        var captureFields=new[]{"enabled","current"}.Select(n=>capture.GetField(n,flags)!).ToArray();
        var saved=fields.Concat(captureFields).ToDictionary(f=>f,f=>f.GetValue(null));
        var oldSetting=diagnosticSetting.GetValue(configuration);
        var main=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="Main");
        var controllerType=main.GetType("GameNoteController",true)!;
        var controller=RuntimeHelpers.GetUninitializedObject(controllerType);
        var baseType=controllerType.BaseType!;
        var map=GameLoader.Load(gameRoot,"""{"version":"3.3.0","colorNotes":[{"b":2,"x":0,"y":0,"c":0,"d":1}]}""",120);
        var note=OracleBridge.GetItems(map).OfType<NoteData>().Single();
        if((int)note.scoringType<=0)throw new InvalidDataException("Note observer guard control needs a real scoring note");
        baseType.GetField("_noteData",flags)!.SetValue(controller,note);
        var session=RuntimeHelpers.GetUninitializedObject(capture.GetNestedType("Session",flags)!);
        var observer=diagnostics.GetMethod("NoteInitialized",flags)!;
        var recorder=diagnostics.GetMethod("RecordNotePose",flags)!;
        Action Compile(MethodInfo target)
        {
            var method=new DynamicMethod("CallGuardedNoteObserver",typeof(void),new[]{typeof(object)},typeof(NoteDiagnosticCostChecks).Module,true);
            var il=method.GetILGenerator();
            if(target==recorder)il.Emit(OpCodes.Ldstr,"offline-budget-control");
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,baseType);
            if(target==recorder){var ordinal=il.DeclareLocal(typeof(int?));il.Emit(OpCodes.Ldloca,ordinal);il.Emit(OpCodes.Initobj,typeof(int?));il.Emit(OpCodes.Ldloc,ordinal);}
            il.Emit(OpCodes.Call,target);il.Emit(OpCodes.Ret);
            return(Action)method.CreateDelegate(typeof(Action),controller);
        }
        var observe=Compile(observer);var record=Compile(recorder);var results=new List<object>();int failed=0;
        try
        {
            foreach(string branch in new[]{"inactive","logging-disabled","diagnostic-limit","note-budget","no-attempt","snapshot-budget"})
            {
                void Set(string name,object value)=>diagnostics.GetField(name,flags)!.SetValue(null,value);
                Set("active",branch!="inactive");Set("mainThread",Environment.CurrentManagedThreadId);Set("emitted",branch=="diagnostic-limit"?512:0);
                Set("noteSamples",branch=="note-budget"?3:0);Set("notePoseSamples",branch=="snapshot-budget"?12:0);
                diagnosticSetting.SetValue(configuration,branch!="logging-disabled");
                captureFields[0].SetValue(null,true);captureFields[1].SetValue(null,branch=="no-attempt"?null:session);
                var call=branch=="snapshot-budget"?record:observe;
                int count=(int)diagnostics.GetField("noteSamples",flags)!.GetValue(null)!;
                int poses=(int)diagnostics.GetField("notePoseSamples",flags)!.GetValue(null)!;
                for(int i=0;i<16384;i++)call();
                const int passes=1048576;
                long before=GC.GetAllocatedBytesForCurrentThread(),started=Stopwatch.GetTimestamp();
                for(int i=0;i<passes;i++)call();
                long ticks=Stopwatch.GetTimestamp()-started,allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                bool unchanged=(int)diagnostics.GetField("noteSamples",flags)!.GetValue(null)! ==count
                    &&(int)diagnostics.GetField("notePoseSamples",flags)!.GetValue(null)! ==poses;
                if(allocated!=0||!unchanged)failed++;
                results.Add(new{branch,passes,allocatedBytes=allocated,unchanged,nanosecondsPerCall=ticks*(1000000000.0/Stopwatch.Frequency)/passes});
            }
        }
        finally
        {
            foreach(var pair in saved)pair.Key.SetValue(null,pair.Value);
            diagnosticSetting.SetValue(configuration,oldSetting);
            ((System.Collections.IList)diagnostics.GetField("sampledNotes",flags)!.GetValue(null)!).Clear();
        }
        return new{failed,cases=results.Count,framework=RuntimeInformation.FrameworkDescription,results,
            scope="Actual packaged typed observer/recorder fast paths on an uninitialized managed controller carrying a real scoring NoteData. Guards must return before native pose access. "
                +"Warm offline managed allocation/CPU only; active Unity snapshot cost, Harmony wrapper, Mono gameplay frames and collider reads are not measured."};
    }
}
