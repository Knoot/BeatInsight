// Only the game data shape used by WallAnalyzer. No Unity assembly is loaded by the CLI.
#pragma warning disable CS0649 // Runtime-independent obstacle shape.
internal sealed class ObstacleData
{
    public int width, height, lineLayer, lineIndex;
    public float beat, endBeat, duration;
}
#pragma warning restore CS0649

namespace BeatInsight.WallAlert
{
    public static class WallAlertAdapter
    {
        public static int Count(string json,float bpm,bool mappingExtensions=false,bool noodleExtensions=false,bool preserveExtendedLayers=false)=>Result(json,bpm,mappingExtensions,noodleExtensions,preserveExtendedLayers).SquatCount;
        static WallAnalysis Result(string json,float bpm,bool mappingExtensions,bool noodleExtensions,bool preserveExtendedLayers=false)=>noodleExtensions?NoodleWallAnalysis.Analyze(json,bpm):mappingExtensions?MappingWallAnalysis.Analyze(json,bpm):WallAnalyzer.AnalyzeCrouchWalls(json,bpm,preserveExtendedLayers);
        public static object Analyze(string json, float bpm,bool mappingExtensions=false,bool noodleExtensions=false)
        {
            var result = Result(json,bpm,mappingExtensions,noodleExtensions);
            return new
            {
                implementation = "built-in-wall-analyzer",
                result.BeatmapFormatVersion, result.SquatCount, result.Timecodes
            };
        }
    }
}
