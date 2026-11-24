using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using BeatInsight.Core;
using UnityEngine;

namespace BeatInsight.Compare;

// Nominal analytic groups derived from real row output and captured native slices.
// This does not execute the Noodle/Heck spawn patch chain or Unity movement.
static class NoodleBurstProjection
{
    public static (MapNote[] Notes,object Evidence) Prepare(string gameRoot,BeatmapData map,float bpm,bool densityOnly=false)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        string mainPath=Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll");
        if(Comparison.HashFile(mainPath)!="A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")throw new InvalidDataException("Reinspect changed chain spawner");
        var main=AssemblyLoadContext.Default.LoadFromAssemblyPath(mainPath);
        var process=main.GetType("BurstSliderSpawner",true)!.GetMethod("ProcessSliderData",flags)!;
        var movementType=main.GetType("VariableMovementDataProvider",true)!;
        var movement=RuntimeHelpers.GetUninitializedObject(movementType);movementType.GetField("_halfJumpDuration",flags)!.SetValue(movement,1f);
        var spawnType=main.GetType("SliderSpawnData",true)!;
        var emitted=new List<NoteData>();var callbackType=process.GetParameters()[4].ParameterType;
        var parameters=callbackType.GetMethod("Invoke")!.GetParameters().Select(p=>Expression.Parameter(p.ParameterType,p.Name)).ToArray();
        var callback=Expression.Lambda(callbackType,Expression.Call(Expression.Constant(emitted),typeof(List<NoteData>).GetMethod("Add")!,parameters[0]),parameters).Compile();
        var noodle=AssemblyLoadContext.Default.Assemblies.Single(a=>a.GetName().Name=="NoodleExtensions");
        var noteCtor=noodle.GetType("NoodleExtensions.NoodleNoteData",true)!.GetConstructors(flags).Single();var cp=noteCtor.GetParameters();
        var sliderCtor=noodle.GetType("NoodleExtensions.NoodleSliderData",true)!.GetConstructors(flags).Single();
        var definitions=Activator.CreateInstance(cp[3].ParameterType)!;var tracks=Activator.CreateInstance(cp[4].ParameterType)!;
        IDictionary Data(BeatmapObjectData item)=>(IDictionary)item.GetType().GetProperty("customData")!.GetValue(item)!;
        bool Fake(BeatmapObjectData item)=>Data(item)["NE_fake"] is true;
        object Parse(BeatmapObjectData item)
        {
            var metadata=Data(item);
            if(densityOnly)
            {
                var nominal=(IDictionary)Activator.CreateInstance(metadata.GetType())!;
                foreach(string key in new[]{"coordinates","tailCoordinates","NE_fake"})if(metadata.Contains(key))nominal[key]=metadata[key];
                metadata=nominal;
            }
            return (item is NoteData?noteCtor:sliderCtor).Invoke(new[]{(object)item,metadata,bpm,definitions,tracks,false,false});
        }
        float? Read(object parsed,string name)=>(float?)parsed.GetType().GetProperty(name,flags)!.GetValue(parsed);
        var all=OracleBridge.GetItems(map).OfType<BeatmapObjectData>().ToArray();
        var heads=all.OfType<NoteData>().Where(n=>!Fake(n)&&(int)n.gameplayType==2).Select(n=>(Note:n,Parsed:Parse(n))).ToArray();
        var replacements=new Dictionary<NoteData,MapNote>();var orphans=new List<MapNote>();var chains=new List<object>();
        foreach(var chain in all.OfType<SliderData>().Where(s=>(int)s.sliderType==1&&!Fake(s)))
        {
            var parsed=Parse(chain);
            float x=Read(parsed,"StartX")??chain.headLineIndex-2f,y=Read(parsed,"StartY")??(float)chain.headLineLayer;
            var candidates=heads.Where(h=>h.Note.time==chain.time&&h.Note.colorType==chain.colorType
                &&(Read(h.Parsed,"StartX")??h.Note.lineIndex-2f)==x&&(Read(h.Parsed,"StartY")??(float)h.Note.noteLineLayer)==y).ToArray();
            if(chain.hasHeadNote&&candidates.Length!=1||!chain.hasHeadNote&&candidates.Length!=0)throw new InvalidDataException("Native chain head binding is ambiguous");
            emitted.Clear();
            var spawn=Activator.CreateInstance(spawnType,new object[]{new Vector3(x,y,0),0f,
                new Vector3(Read(parsed,"TailStartX")??chain.tailLineIndex-2f,Read(parsed,"TailStartY")??(float)chain.tailLineLayer,0),0f})!;
            process.Invoke(null,new[]{chain,spawn,(object)false,movement,callback});
            if(emitted.Count!=chain.sliceCount-1||emitted.Any(n=>(int)n.cutDirection!=8))throw new InvalidDataException("Native chain slice stream changed");
            if(chain.hasHeadNote)
            {
                if(!replacements.TryAdd(candidates[0].Note,new MapNote {Time=chain.time,Beat=chain.beat,X=chain.headLineIndex,Y=(int)chain.headLineLayer,
                    Color=(int)chain.colorType,Direction=(int)chain.headCutDirection,SliceCount=1+emitted.Count}))throw new InvalidDataException("Native physical head shared by chains");
            }
            else
            {
                if(emitted.Count==0)throw new InvalidDataException("Missing head and no native chain slices");
                var first=emitted[0];orphans.Add(new MapNote {Time=first.time,Beat=first.beat,X=first.lineIndex,Y=(int)first.noteLineLayer,
                    Color=(int)first.colorType,Direction=(int)first.cutDirection,SliceCount=emitted.Count});
            }
            chains.Add(new{chain.time,chain.tailTime,chain.hasHeadNote,chain.sliceCount,emittedSlices=emitted.Count,
                firstSlice=emitted.Count==0?(float?)null:emitted[0].time,lastSlice=emitted.Count==0?(float?)null:emitted[^1].time});
        }
        if(replacements.Count!=heads.Length)throw new InvalidDataException("Unmatched native physical chain head");
        var notes=new List<MapNote>();
        foreach(var note in all.OfType<NoteData>().Where(n=>!Fake(n)))
        {
            if(replacements.TryGetValue(note,out var replacement)){notes.Add(replacement);continue;}
            if((int)note.scoringType>0&&(int)note.scoringType!=4&&(int)note.scoringType!=7)
                notes.Add(new MapNote {Time=note.time,Beat=note.beat,X=note.lineIndex,Y=(int)note.noteLineLayer,Color=(int)note.colorType,Direction=(int)note.cutDirection,ScoringType=(int)note.scoringType});
        }
        notes.AddRange(orphans);
        return(notes.OrderBy(n=>n.Time).ToArray(),new{chains,physicalHeads=heads.Length,orphans=orphans.Count,densityOnly,
            scope="Actual loaded gameplayType heads, native Noodle coordinate consumers and unpatched BurstSliderSpawner callbacks; analytic one-group-per-chain projection, controlled spawn vectors/half jump; no Noodle/Heck spawn patch chain or Unity lifecycle"});
    }
}
