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
        public static int Count(string json,float bpm)=>WallAnalyzer.AnalyzeCrouchWalls(json,bpm).SquatCount;
        public static object Analyze(string json, float bpm)
        {
            var result = WallAnalyzer.AnalyzeCrouchWalls(json, bpm);
            return new
            {
                implementation = "built-in-wall-analyzer",
                result.BeatmapFormatVersion, result.SquatCount, result.Timecodes
            };
        }
    }
}
