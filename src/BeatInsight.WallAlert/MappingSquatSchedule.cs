using System;
using System.Collections.Generic;
using System.Linq;

namespace BeatInsight.WallAlert
{
    public readonly struct SquatWallData
    {
        public readonly float Time,Beat,EndBeat;
        public readonly int Lane,Layer,Width,Height;
        public SquatWallData(float time,float beat,float endBeat,int lane,int layer,int width,int height)
        {Time=time;Beat=beat;EndBeat=endBeat;Lane=lane;Layer=layer;Width=width;Height=height;}
    }

    // Build before playback from the actual loaded/transformed obstacles.
    // Returned indices refer to the input snapshot, never to a menu map cache.
    public static class MappingSquatSchedule
    {
        readonly struct Part
        {
            internal readonly int Index;
            internal readonly float Beat,End;
            internal readonly long Left,Right,Bottom;
            internal Part(int index,SquatWallData wall,long left,long right,long bottom)
            {Index=index;Beat=wall.Beat;End=wall.EndBeat;Left=left;Right=right;Bottom=bottom;}
        }
        public static int[] Build(IReadOnlyList<SquatWallData> walls,float minimumTime=float.NegativeInfinity)
        {
            var parts=new List<Part>();
            for(int i=0;i<walls.Count;i++)
            {
                var wall=walls[i];
                if(!float.IsFinite(wall.Time)||!(wall.Time>=minimumTime)||!float.IsFinite(wall.Beat)||!float.IsFinite(wall.EndBeat)||wall.EndBeat<wall.Beat)continue;
                long bottom=MappingWallAnalysis.Layer(wall.Layer),top=bottom+MappingWallAnalysis.Size(wall.Height),
                    left=MappingWallAnalysis.Position(wall.Lane),right=left+MappingWallAnalysis.Size(wall.Width);
                if(right>left&&bottom<=2000&&top>2000)parts.Add(new Part(i,wall,left,right,bottom));
            }
            var ordered=parts.OrderBy(p=>p.Beat).ThenBy(p=>p.Index).ToArray();
            var active=new List<Part>();var triggers=new List<int>();int next=0;
            float previous=float.MinValue;
            foreach(var candidate in ordered.Where(p=>p.Bottom>0).GroupBy(p=>p.Beat).Select(g=>g.First()))
            {
                float beat=candidate.Beat;
                active.RemoveAll(p=>p.End+WallAnalyzer.CrouchWallCoverageToleranceBeats<beat);
                while(next<ordered.Length&&ordered[next].Beat<=beat)active.Add(ordered[next++]);
                long covered=1000;
                foreach(var part in active.Where(p=>p.End+WallAnalyzer.CrouchWallCoverageToleranceBeats>=beat).OrderBy(p=>p.Left))
                {
                    if(part.Left>covered)break;
                    covered=Math.Max(covered,part.Right);
                }
                if(covered<3000)continue;
                if(beat>previous+WallAnalyzer.CrouchWallSequenceToleranceBeats)triggers.Add(candidate.Index);
                previous=beat;
            }
            return triggers.ToArray();
        }
    }

    // One bit per prepared trigger; no geometry, sorting or allocations at signal time.
    public sealed class SquatWarningPlayback
    {
        readonly bool[] consumed;
        public SquatWarningPlayback(int triggerCount){consumed=new bool[triggerCount];}
        public bool Observe(int trigger)
        {
            if((uint)trigger>=(uint)consumed.Length||consumed[trigger])return false;
            consumed[trigger]=true;return true;
        }
        public void Reset()=>Array.Clear(consumed,0,consumed.Length);
    }
}
