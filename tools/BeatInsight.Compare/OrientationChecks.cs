using BeatInsight.Formats;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Compare;

static class OrientationChecks
{
    public static object Run(string gameRoot,out int failures)
    {
        string folder=Path.Combine(AppContext.BaseDirectory,"orientation-fixtures");Directory.CreateDirectory(folder);
        var notes=new JArray();int rows=0;
        foreach(int color in new[]{0,1})foreach(int direction in Enumerable.Range(0,9))
        foreach(int otherDirection in new[]{direction,8,(direction+1)%8}.Distinct())
        foreach(int x in Enumerable.Range(0,4))foreach(int y in Enumerable.Range(0,3))
        {
            if(x==1&&y==0)continue;
            float beat=2+rows++*4;
            notes.Add(new JObject { ["b"]=beat,["x"]=1,["y"]=0,["c"]=color,["d"]=direction,["a"]=17 });
            notes.Add(new JObject { ["b"]=beat,["x"]=x,["y"]=y,["c"]=color,["d"]=otherDirection,["a"]=-13 });
        }
        var json=new JObject {["version"]="3.3.0",["colorNotes"]=notes};
        var info=JObject.Parse("""{"version":"4.0.1","audio":{"bpm":120,"songDuration":2000},"difficultyBeatmaps":[{"characteristic":"Standard","difficulty":"Expert","beatmapDataFilename":"map.dat"}]}""");
        File.WriteAllText(Path.Combine(folder,"Info.dat"),info.ToString());File.WriteAllText(Path.Combine(folder,"map.dat"),json.ToString());
        var own=LocalMap.Load(LocalMap.Cases(folder).Single());
        var data=GameLoader.Load(gameRoot,own.Json,own.Bpm);
        var native=OracleBridge.GetItems(data).OfType<NoteData>().ToDictionary(n=>(n.beat,n.lineIndex,(int)n.noteLineLayer,(int)n.colorType,(int)n.cutDirection));
        failures=0;var details=new List<object>();
        foreach(var note in own.Input.Notes)
        {
            var original=native[(note.Beat,note.X,note.Y,note.Color,note.Direction)];
            if(!note.CutDirectionAngleOffset.HasValue||Math.Abs(note.CutDirectionAngleOffset.Value-original.cutDirectionAngleOffset)>1e-5)
            {
                failures++;
                if(details.Count<20)details.Add(new {note.Beat,note.X,note.Y,note.Color,note.Direction,own=note.CutDirectionAngleOffset,original=original.cutDirectionAngleOffset});
            }
        }
        return new {rows,notes=own.Input.Notes.Length,failures,details,scope="per-hand two-note auto orientation; same/different directions, arrows/dots, all lanes/layers and explicit offsets"};
    }
}
