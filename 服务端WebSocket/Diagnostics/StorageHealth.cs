namespace GrandUMI.Diagnostics;

public sealed record StorageHealthSnapshot(
    bool Healthy,
    string Reason,
    long TotalBytes,
    long AvailableBytes);

public static class StorageHealth
{
    private const long DefaultMinimumAvailableBytes = 2L * 1024 * 1024 * 1024;
    private const double DefaultMinimumAvailableFraction = 0.05;
    private static readonly object CacheLock = new();
    private static StorageHealthSnapshot? _cached;
    private static long _cacheExpiresAt;

    public static StorageHealthSnapshot GetCurrent()
    {
        var now = Environment.TickCount64;
        lock (CacheLock)
        {
            if (_cached is not null && now < _cacheExpiresAt) return _cached;

            var dataDirectory = Environment.GetEnvironmentVariable("GRANDUMI_DATA_DIR");
            if (string.IsNullOrWhiteSpace(dataDirectory))
                dataDirectory = Path.GetDirectoryName(Game.Logging.MatchLogRecorder.GetLogDir());

            _cached = Inspect(
                dataDirectory ?? AppContext.BaseDirectory,
                ReadPositiveLong("GRANDUMI_MIN_FREE_BYTES", DefaultMinimumAvailableBytes),
                ReadFraction("GRANDUMI_MIN_FREE_FRACTION", DefaultMinimumAvailableFraction));
            _cacheExpiresAt = now + 5_000;
            return _cached;
        }
    }

    internal static StorageHealthSnapshot Inspect(
        string dataDirectory,
        long minimumAvailableBytes,
        double minimumAvailableFraction)
    {
        try
        {
            var fullPath = Path.GetFullPath(dataDirectory);
            if (!Directory.Exists(fullPath))
                return new(false, "storage_unavailable", 0, 0);

            var drive = FindContainingDrive(fullPath);
            if (drive is null || !drive.IsReady)
                return new(false, "storage_unavailable", 0, 0);

            var total = drive.TotalSize;
            var available = drive.AvailableFreeSpace;
            var required = Math.Max(
                minimumAvailableBytes,
                (long)Math.Ceiling(total * minimumAvailableFraction));
            if (available < required)
                return new(false, "storage_pressure", total, available);

            var probePath = Path.Combine(fullPath, $".storage-health-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
            try
            {
                using var probe = new FileStream(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
                probe.WriteByte(1);
                probe.Flush(flushToDisk: true);
            }
            finally
            {
                try { File.Delete(probePath); } catch { }
            }

            return new(true, "", total, available);
        }
        catch
        {
            return new(false, "storage_unwritable", 0, 0);
        }
    }

    private static DriveInfo? FindContainingDrive(string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return DriveInfo.GetDrives()
            .Where(drive => IsWithin(fullPath, drive.RootDirectory.FullName, comparison))
            .OrderByDescending(drive => drive.RootDirectory.FullName.Length)
            .FirstOrDefault();
    }

    private static bool IsWithin(string path, string root, StringComparison comparison)
    {
        var normalizedRoot = Path.GetFullPath(root);
        if (path.Equals(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), comparison)) return true;
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
            normalizedRoot += Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, comparison);
    }

    private static long ReadPositiveLong(string name, long fallback)
        => long.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0
            ? value
            : fallback;

    private static double ReadFraction(string name, double fallback)
        => double.TryParse(
               Environment.GetEnvironmentVariable(name),
               System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture,
               out var value)
           && value is > 0 and < 1
            ? value
            : fallback;
}
