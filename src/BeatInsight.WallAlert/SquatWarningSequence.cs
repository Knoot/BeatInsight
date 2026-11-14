namespace BeatInsight.WallAlert
{
    // Uses the same wall classification and coverage policy as the menu count.
    public sealed class SquatWarningSequence
    {
        readonly float[] laneEnds = new float[4];
        readonly global::ObstacleData obstacle = new global::ObstacleData();
        float lastBeat = float.MinValue;

        public void Reset()
        {
            System.Array.Clear(laneEnds, 0, laneEnds.Length);
            lastBeat = float.MinValue;
        }

        public bool Observe(float beat, float endBeat, float duration, int lane, int layer, int width, int height)
        {
            obstacle.beat=beat; obstacle.endBeat=endBeat; obstacle.duration=duration;
            obstacle.lineIndex=lane; obstacle.lineLayer=layer; obstacle.width=width; obstacle.height=height;
            if(!global::WallAlert.Services.WallAnalyzer.ApplyLaneCoverage(laneEnds, obstacle)
                || !global::WallAlert.Services.WallAnalyzer.IsUpperCrouchWallPart(obstacle))return false;
            bool show=beat>lastBeat+global::WallAlert.Services.WallAnalyzer.CrouchWallSequenceToleranceBeats;
            lastBeat=beat;
            return show;
        }
    }
}
