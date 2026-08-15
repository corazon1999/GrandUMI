using System.Text;
using System.Text.Json;

namespace GrandUMI.Persistence;

/// <summary>阻止同一数据目录被两个后端进程同时写入。</summary>
internal sealed class SingleWriterLease : IDisposable
{
    private readonly FileStream _stream;
    internal string LeasePath { get; }

    private SingleWriterLease(FileStream stream, string leasePath)
    {
        _stream = stream;
        LeasePath = leasePath;
    }

    internal static bool IsRequired
        => string.Equals(
            Environment.GetEnvironmentVariable("GRANDUMI_REQUIRE_SINGLE_WRITER"),
            "1",
            StringComparison.Ordinal);

    internal static SingleWriterLease Acquire(string dataDirectory, string nodeId)
    {
        if (OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("GrandUMI 单写者租约暂不支持 macOS。");
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, ".grandumi-writer.lock");
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            stream.Lock(0, 1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"数据目录已有活动写入进程，拒绝双写：{Path.GetFullPath(dataDirectory)}", ex);
        }

        var payload = JsonSerializer.Serialize(new
        {
            nodeId,
            processId = Environment.ProcessId,
            acquiredAtUtc = DateTime.UtcNow,
        });
        stream.SetLength(0);
        stream.Write(Encoding.UTF8.GetBytes(payload));
        stream.Flush(flushToDisk: true);
        return new SingleWriterLease(stream, path);
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsMacOS())
        {
            try { _stream.Unlock(0, 1); } catch { }
        }
        _stream.Dispose();
    }
}
