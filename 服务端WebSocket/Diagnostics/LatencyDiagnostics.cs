using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace GrandUMI.Diagnostics;

/// <summary>
/// 轻量慢路径观测。默认只打印超过 80ms 的阶段，可通过
/// GRANDUMI_SLOW_MS 调整阈值，不记录卡组或手牌等敏感内容。
/// </summary>
public static class LatencyDiagnostics
{
    private static readonly double SlowThresholdMs = ReadThreshold();
    private static readonly TimeSpan SummaryInterval = ReadSummaryInterval();
    private static readonly ConcurrentDictionary<MetricKey, MetricSeries> Metrics = new();
    private static readonly Timer SummaryTimer = new(_ => FlushSummary(), null, SummaryInterval, SummaryInterval);

    // 固定桶不做排序和样本留存，避免观测本身给高峰期制造 GC 压力。
    private static readonly double[] BucketUpperBounds =
        { 1, 2, 5, 10, 20, 40, 80, 160, 320, 640, 1_000, 2_000, 5_000, double.PositiveInfinity };

    public static long Start() => Stopwatch.GetTimestamp();

    public static double ElapsedMs(long startedAt)
        => Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

    public static void Observe(string stage, long startedAt, string detail = "")
    {
        var elapsedMs = ElapsedMs(startedAt);
        var series = RecordValue(stage, elapsedMs, "ms");
        if (elapsedMs < SlowThresholdMs || !series.TryTakeSlowSample()) return;

        var suffix = string.IsNullOrWhiteSpace(detail) ? "" : $"，{detail}";
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [延迟] {stage} {elapsedMs:F1}ms{suffix}");
    }

    /// <summary>记录队列深度、消息体积等非耗时指标，并纳入周期分位数摘要。</summary>
    public static void RecordMetric(string stage, double value, string unit)
        => RecordValue(stage, value, unit);

    /// <summary>导出进程生命周期内的累计直方图，供 Prometheus 抓取。</summary>
    public static string ExportPrometheus()
    {
        var output = new StringBuilder();
        output.AppendLine("# HELP grandumi_observation_value Server latency and queue observations.");
        output.AppendLine("# TYPE grandumi_observation_value histogram");
        foreach (var series in Metrics.Values.OrderBy(item => item.Key.Stage, StringComparer.Ordinal))
        {
            var snapshot = series.Snapshot();
            var stage = EscapeLabel(series.Key.Stage);
            var unit = EscapeLabel(series.Key.Unit);
            long cumulative = 0;
            for (var i = 0; i < snapshot.Buckets.Length; i++)
            {
                cumulative += snapshot.Buckets[i];
                var upper = double.IsPositiveInfinity(BucketUpperBounds[i])
                    ? "+Inf"
                    : BucketUpperBounds[i].ToString(CultureInfo.InvariantCulture);
                output.Append("grandumi_observation_value_bucket{stage=\"").Append(stage)
                    .Append("\",unit=\"").Append(unit).Append("\",le=\"").Append(upper)
                    .Append("\"} ").Append(cumulative).AppendLine();
            }
            output.Append("grandumi_observation_value_count{stage=\"").Append(stage)
                .Append("\",unit=\"").Append(unit).Append("\"} ").Append(snapshot.Count).AppendLine();
            output.Append("grandumi_observation_value_max{stage=\"").Append(stage)
                .Append("\",unit=\"").Append(unit).Append("\"} ")
                .Append(snapshot.Max.ToString("F3", CultureInfo.InvariantCulture)).AppendLine();
        }
        return output.ToString();
    }

    private static string EscapeLabel(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static MetricSeries RecordValue(string stage, double value, string unit)
    {
        var series = Metrics.GetOrAdd(new MetricKey(stage, unit), static key => new MetricSeries(key));
        series.Record(value);
        return series;
    }

    private static void FlushSummary()
    {
        foreach (var series in Metrics.Values)
        {
            var snapshot = series.Drain();
            if (snapshot.Count == 0) continue;
            Console.WriteLine(
                $"[{DateTime.Now:HH:mm:ss}] [延迟汇总] {series.Key.Stage} " +
                $"次数={snapshot.Count}，P50={snapshot.P50:F1}{series.Key.Unit}，" +
                $"P95={snapshot.P95:F1}{series.Key.Unit}，P99={snapshot.P99:F1}{series.Key.Unit}，" +
                $"最大={snapshot.Max:F1}{series.Key.Unit}");
        }
    }

    private static double ReadThreshold()
    {
        var raw = Environment.GetEnvironmentVariable("GRANDUMI_SLOW_MS");
        return double.TryParse(raw, out var value) && value >= 1 ? value : 80;
    }

    private static TimeSpan ReadSummaryInterval()
    {
        var raw = Environment.GetEnvironmentVariable("GRANDUMI_LATENCY_SUMMARY_SECONDS");
        return int.TryParse(raw, out var seconds) && seconds is >= 10 and <= 3_600
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMinutes(1);
    }

    private readonly record struct MetricKey(string Stage, string Unit);

    private sealed class MetricSeries(MetricKey key)
    {
        private readonly long[] _buckets = new long[BucketUpperBounds.Length];
        private readonly long[] _summaryBuckets = new long[BucketUpperBounds.Length];
        private long _count;
        private long _summaryCount;
        private long _maxMicrounits;
        private long _summaryMaxMicrounits;
        private int _slowSamplesRemaining = 3;

        public MetricKey Key { get; } = key;

        public void Record(double value)
        {
            var safeValue = double.IsFinite(value) ? Math.Max(0, value) : 0;
            var bucket = Array.FindIndex(BucketUpperBounds, upper => safeValue <= upper);
            if (bucket < 0) bucket = BucketUpperBounds.Length - 1;
            Interlocked.Increment(ref _buckets[bucket]);
            Interlocked.Increment(ref _summaryBuckets[bucket]);
            Interlocked.Increment(ref _count);
            Interlocked.Increment(ref _summaryCount);

            var microunits = (long)Math.Min(long.MaxValue, safeValue * 1_000);
            UpdateMax(ref _maxMicrounits, microunits);
            UpdateMax(ref _summaryMaxMicrounits, microunits);
        }

        public bool TryTakeSlowSample()
        {
            while (true)
            {
                var current = Volatile.Read(ref _slowSamplesRemaining);
                if (current <= 0) return false;
                if (Interlocked.CompareExchange(ref _slowSamplesRemaining, current - 1, current) == current)
                    return true;
            }
        }

        public MetricSnapshot Drain()
        {
            var counts = new long[_summaryBuckets.Length];
            long total = 0;
            for (var i = 0; i < _summaryBuckets.Length; i++)
            {
                counts[i] = Interlocked.Exchange(ref _summaryBuckets[i], 0);
                total += counts[i];
            }

            Interlocked.Exchange(ref _summaryCount, 0);
            var max = Interlocked.Exchange(ref _summaryMaxMicrounits, 0) / 1_000d;
            Interlocked.Exchange(ref _slowSamplesRemaining, 3);
            return total == 0
                ? default
                : new MetricSnapshot(total, Percentile(counts, total, 0.50),
                    Percentile(counts, total, 0.95), Percentile(counts, total, 0.99), max);
        }

        public MetricExportSnapshot Snapshot()
        {
            var counts = new long[_buckets.Length];
            for (var i = 0; i < counts.Length; i++) counts[i] = Interlocked.Read(ref _buckets[i]);
            return new MetricExportSnapshot(
                Interlocked.Read(ref _count),
                Interlocked.Read(ref _maxMicrounits) / 1_000d,
                counts);
        }

        private static void UpdateMax(ref long target, long value)
        {
            var current = Interlocked.Read(ref target);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current) return;
                current = observed;
            }
        }

        private static double Percentile(IReadOnlyList<long> counts, long total, double percentile)
        {
            var target = Math.Max(1, (long)Math.Ceiling(total * percentile));
            long cumulative = 0;
            for (var i = 0; i < counts.Count; i++)
            {
                cumulative += counts[i];
                if (cumulative >= target)
                    return double.IsPositiveInfinity(BucketUpperBounds[i])
                        ? BucketUpperBounds[^2]
                        : BucketUpperBounds[i];
            }
            return BucketUpperBounds[^2];
        }
    }

    private readonly record struct MetricSnapshot(long Count, double P50, double P95, double P99, double Max);
    private readonly record struct MetricExportSnapshot(long Count, double Max, long[] Buckets);
}
