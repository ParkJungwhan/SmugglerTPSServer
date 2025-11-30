using System.Diagnostics;

namespace SmugglerServer.Lib;

public readonly struct SteadyTimePoint
{
    public readonly long Timestamp;

    public SteadyTimePoint(long ts) => Timestamp = ts;

    public long TimeSinceEpochMs =>
        (Timestamp - SteadyClock.StartTimestamp) * 1000 / Stopwatch.Frequency;

    public static long DiffMilliseconds(SteadyTimePoint end, SteadyTimePoint start)
    {
        long diff = end.Timestamp - start.Timestamp;
        return diff * 1000 / Stopwatch.Frequency;
    }
}

public static class SteadyClock
{
    internal static readonly long StartTimestamp = Stopwatch.GetTimestamp();

    public static SteadyTimePoint Now() =>
        new SteadyTimePoint(Stopwatch.GetTimestamp());
}