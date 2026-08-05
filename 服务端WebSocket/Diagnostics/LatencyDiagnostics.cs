using System.Diagnostics;

namespace GrandUMI.Diagnostics;

/// <summary>
/// 轻量慢路径观测。默认只打印超过 80ms 的阶段，可通过
/// GRANDUMI_SLOW_MS 调整阈值，不记录卡组或手牌等敏感内容。
/// </summary>
public static class LatencyDiagnostics
{
    private static readonly double SlowThresholdMs = ReadThreshold();

    public static long Start() => Stopwatch.GetTimestamp();

    public static double ElapsedMs(long startedAt)
        => Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

    public static void Observe(string stage, long startedAt, string detail = "")
    {
        var elapsedMs = ElapsedMs(startedAt);
        if (elapsedMs < SlowThresholdMs) return;

        var suffix = string.IsNullOrWhiteSpace(detail) ? "" : $"，{detail}";
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [延迟] {stage} {elapsedMs:F1}ms{suffix}");
    }

    private static double ReadThreshold()
    {
        var raw = Environment.GetEnvironmentVariable("GRANDUMI_SLOW_MS");
        return double.TryParse(raw, out var value) && value >= 1 ? value : 80;
    }
}
