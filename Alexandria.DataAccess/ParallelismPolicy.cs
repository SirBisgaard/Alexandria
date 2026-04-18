namespace Alexandria.DataAccess;

/// <summary>
/// Central parallelism policy for CPU/IO-bound batch work — ffmpeg chapter transcodes,
/// per-file ID3 writes, ffprobe fan-out. Caps concurrency at 80 % of the host's logical
/// cores, floored to 1 and capped at 8 so a 64-core machine doesn't spawn 64 concurrent
/// ffmpeg processes against the same M4B — which in production saturated the library
/// mount and produced AVERROR_EOF failures.
/// </summary>
public static class ParallelismPolicy
{
    public static int MaxDegree => Math.Clamp((int)(Environment.ProcessorCount * 0.8), 1, 8);
}
