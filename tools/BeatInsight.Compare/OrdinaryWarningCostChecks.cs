using System.Diagnostics;
using System.Runtime.InteropServices;
using BeatInsight.WallAlert;

namespace BeatInsight.Compare;

static class OrdinaryWarningCostChecks
{
    static int Observe(SquatWarningSequence sequence,SquatWallData[] walls,int start=0)
    {
        int signals=0;
        for(int i=start;i<walls.Length;i++)
        {
            var w=walls[i];
            if(sequence.Observe(w.Beat,w.EndBeat,w.EndBeat-w.Beat,w.Lane,w.Layer,w.Width,w.Height))signals++;
        }
        return signals;
    }

    internal static object Run()
    {
        var results=new List<object>();int failed=0;
        foreach(int count in new[]{64,4096,65536})
        {
            var walls=new SquatWallData[count];
            for(int i=0;i<count;i++)
            {
                int part=i%8;float beat=i/8*4+(part switch{0 or 1=>0,2=>.5f,3=>1.001f,4=>2,5=>2.25f,6=>3,_=>3.5f});
                int lane=part==0?1:part==1?2:part==4?0:part==5?3:1;
                int width=part is 0 or 1 or 5?1:part==4?4:part==6?0:2;
                walls[i]=new SquatWallData(beat/2,beat,beat+2,lane,part==4?0:1,width,part==7?0:3);
            }
            var sequence=new SquatWarningSequence();int expected=Observe(sequence,walls);
            for(int i=0;i<16;i++){sequence.Reset();Observe(sequence,walls);}
            int passes=Math.Max(16,2097152/count),signals=0;
            long before=GC.GetAllocatedBytesForCurrentThread(),started=Stopwatch.GetTimestamp();
            for(int pass=0;pass<passes;pass++){sequence.Reset();signals+=Observe(sequence,walls);}
            long ticks=Stopwatch.GetTimestamp()-started,bytes=GC.GetAllocatedBytesForCurrentThread()-before;
            bool stable=signals==expected*passes;
            if(bytes!=0||!stable)failed++;
            results.Add(new{walls=count,passes,observations=(long)count*passes,signalsPerPass=expected,allocatedBytes=bytes,stable,
                nanosecondsPerWall=ticks*(1000000000.0/Stopwatch.Frequency)/((long)count*passes)});
        }
        // A schedule containing just the first trigger of a group is not a
        // substitute for the sequence on arbitrary delivered callback subsets.
        var grouped=new[]{new SquatWallData(.5f,1,2,1,1,2,3),new SquatWallData(.625f,1.25f,2.25f,1,1,2,3),new SquatWallData(.75f,1.5f,2.5f,1,1,2,3)};
        var full=new SquatWarningSequence();var firstTriggers=new HashSet<int>();
        for(int i=0;i<grouped.Length;i++)
        {var w=grouped[i];if(full.Observe(w.Beat,w.EndBeat,w.EndBeat-w.Beat,w.Lane,w.Layer,w.Width,w.Height))firstTriggers.Add(i);}
        var reset=new SquatWarningSequence();int liveSubset=Observe(reset,grouped,1),preparedSubset=Enumerable.Range(1,2).Count(firstTriggers.Contains);
        bool counterexample=liveSubset==1&&preparedSubset==0;
        if(!counterexample)failed++;
        return new{cases=results.Count+1,failed,framework=RuntimeInformation.FrameworkDescription,results,
            subsetControl=new{liveSubset,preparedSubset,counterexample},
            scope="Offline ordinary SquatWarningSequence only: warmed managed CPU timing/allocation, fixed four-lane state, no scene scans. Timing is descriptive, not a pass threshold or Unity frame/GPU measurement. Callback-subset control does not claim a particular native seek dispatch order; it identifies a requirement for any future precomputed replacement."};
    }
}
