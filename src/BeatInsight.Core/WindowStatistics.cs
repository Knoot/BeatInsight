using System;
using System.Linq;

namespace BeatInsight.Core;

// Input is accepted swings per second, NOT note counts. Grouping belongs to the adapter.
public static class WindowStatistics
{
    public const string ActiveHandMedianPolicy = "per-hand-active-song-second-windows-first-to-last-cube-equal-partial-weight-no-speed-scaling";

    // The input uses the same fixed song-second grid as reference implementation. All positive
    // bins necessarily lie between this hand's first and last accepted cube.
    // Excluding zeros also removes the intro, pauses and any wall-only tail.
    public static double ActiveWindowMedian(int[] counts)
    {
        if (counts == null) throw new ArgumentNullException(nameof(counts));
        if (counts.Any(x => x < 0)) throw new ArgumentOutOfRangeException(nameof(counts));
        return Median(counts.Where(x => x > 0).ToArray());
    }

    public static double Median(int[] counts)
    {
        if (counts == null) throw new ArgumentNullException(nameof(counts));
        if (counts.Any(x => x < 0)) throw new ArgumentOutOfRangeException(nameof(counts));
        if (counts.Length == 0) return 0;
        var sorted = counts.OrderBy(x => x).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : ((double)sorted[mid - 1] + sorted[mid]) / 2;
    }

    // Independently implemented from the observed IL; parity with the original is not yet proven.
    public static double ActiveWindowAverage(int[] counts)
    {
        var active = counts.Where(x => x > 0).ToArray();
        return active.Length == 0 ? 0 : active.Average();
    }
}
