using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrandUMI.Training;

public sealed class ArtifactReplayProcessException : Exception
{
    public ArtifactReplayProcessException(
        string reasonCode,
        string message,
        bool systemicProtocolFailure,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ReasonCode = reasonCode;
        SystemicProtocolFailure = systemicProtocolFailure;
    }

    public string ReasonCode { get; }
    public bool SystemicProtocolFailure { get; }
}

public sealed record ArtifactReplayWorkerProbe(
    string ArtifactId,
    string ArtifactFingerprint,
    string WorkerId,
    string RuntimeManifestHash,
    string StableHash);

/// <summary>
/// 当前进程到不可变历史归档的独立进程代理。可执行文件、参数和工作目录只来自已验证
/// manifest；请求中的 executable 或日志内容绝不会参与进程启动。
/// </summary>
public sealed class ProcessArtifactReplayWorker : IArtifactReplayWorker
{
    public const int DefaultMaximumRequestBytes = 32 * 1024 * 1024;
    public const int DefaultMaximumResponseBytes = 16 * 1024 * 1024;
    public const int DefaultMaximumStderrBytes = 1024 * 1024;
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan DefaultExecutionTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan TerminationWaitTimeout = TimeSpan.FromSeconds(10);

    private readonly VerifiedReplayArtifactArchive _archive;
    private readonly string _dotnetExecutable;
    private readonly TimeSpan _executionTimeout;
    private readonly int _maximumRequestBytes;
    private readonly int _maximumResponseBytes;
    private readonly int _maximumStderrBytes;
    private readonly Func<ProcessStartInfo>? _testStartInfoFactory;

    public ProcessArtifactReplayWorker(
        VerifiedReplayArtifactArchive archive,
        string? trustedDotnetExecutable = null,
        TimeSpan? executionTimeout = null,
        int maximumRequestBytes = DefaultMaximumRequestBytes,
        int maximumResponseBytes = DefaultMaximumResponseBytes,
        int maximumStderrBytes = DefaultMaximumStderrBytes)
    {
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        var entrypoint = archive.Manifest.ReplayWorkerEntrypoint;
        if (!entrypoint.Available)
            throw new ReplayArtifactArchiveException(
                $"归档没有可用 replay worker：{archive.Manifest.ArtifactId}");
        // Verify 已冻结入口；此处再次防止未来调用方绕过 catalog 后直接构造代理。
        _ = ReplayArtifactArchive.Verify(archive.ManifestPath);

        _dotnetExecutable = ResolveTrustedDotnet(
            trustedDotnetExecutable ?? entrypoint.Executable!);
        _executionTimeout = executionTimeout ?? DefaultExecutionTimeout;
        if (_executionTimeout <= TimeSpan.Zero || _executionTimeout > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(executionTimeout));
        if (maximumRequestBytes is <= 0 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumRequestBytes));
        if (maximumResponseBytes is <= 0 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
        if (maximumStderrBytes is <= 0 or > 8 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumStderrBytes));
        _maximumRequestBytes = maximumRequestBytes;
        _maximumResponseBytes = maximumResponseBytes;
        _maximumStderrBytes = maximumStderrBytes;

        var descriptor = ReplayArtifactArchive.CreateTestDescriptor(archive.Manifest);
        EngineArtifactId = descriptor.EngineArtifactId;
        ArtifactFingerprint = ReplayArtifactIdentity.Fingerprint(descriptor);
        WorkerId = ArtifactReplayProcessProtocol.WorkerId(EngineArtifactId);
        _testStartInfoFactory = null;
    }

    internal ProcessArtifactReplayWorker(
        VerifiedReplayArtifactArchive archive,
        TimeSpan executionTimeout,
        int maximumRequestBytes,
        int maximumResponseBytes,
        int maximumStderrBytes,
        Func<ProcessStartInfo> testStartInfoFactory)
        : this(
            archive,
            trustedDotnetExecutable: "dotnet",
            executionTimeout,
            maximumRequestBytes,
            maximumResponseBytes,
            maximumStderrBytes)
    {
        _testStartInfoFactory = testStartInfoFactory
            ?? throw new ArgumentNullException(nameof(testStartInfoFactory));
    }

    public string WorkerId { get; }
    public string EngineArtifactId { get; }
    public string ArtifactFingerprint { get; }

    public Task<ArtifactReplayWorkerProbe> ProbeAsync(
        CancellationToken cancellationToken = default)
        => ProbeAsync(DefaultProbeTimeout, cancellationToken);

    public async Task<ArtifactReplayWorkerProbe> ProbeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        var server = await ExchangeAsync(
            new ArtifactReplayProcessClientFrame(
                ArtifactReplayProcessProtocol.Schema,
                ArtifactReplayProcessProtocol.ProbeKind,
                Request: null),
            timeout,
            cancellationToken);
        if (!string.Equals(server.Kind, ArtifactReplayProcessProtocol.ReadyKind, StringComparison.Ordinal)
            || server.Response is not null
            || server.Probe is null
            || !ArtifactReplayProcessProtocol.IsValidProbe(
                server.Probe,
                EngineArtifactId,
                ArtifactFingerprint,
                WorkerId,
                _archive.Manifest.RuntimeIdentity.ManifestHash))
            throw Protocol("worker 握手确认帧无效。");
        return server.Probe;
    }

    public async Task<ArtifactReplayWorkerResponse> ExecuteAsync(
        ArtifactReplayWorkerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var server = await ExchangeAsync(
            new ArtifactReplayProcessClientFrame(
                ArtifactReplayProcessProtocol.Schema,
                ArtifactReplayProcessProtocol.ExecuteKind,
                request),
            _executionTimeout,
            cancellationToken);
        if (!string.Equals(server.Kind, ArtifactReplayProcessProtocol.ResultKind, StringComparison.Ordinal)
            || server.Probe is not null
            || server.Response is null)
            throw Protocol("worker 结果帧的 kind/payload 互斥关系无效。");
        return server.Response;
    }

    private async Task<ArtifactReplayProcessServerFrame> ExchangeAsync(
        ArtifactReplayProcessClientFrame clientFrame,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var requestBytes = ArtifactReplayProcessProtocol.Serialize(clientFrame);
        if (requestBytes.Length > _maximumRequestBytes)
            throw new ArtifactReplayProcessException(
                ReplayQuarantineCodes.WorkerInputTooLarge,
                $"worker 请求超过 {_maximumRequestBytes} 字节上限。",
                systemicProtocolFailure: false);

        using var wallClock = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wallClock.CancelAfter(timeout);
        using var stderrOverflow = new CancellationTokenSource();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            wallClock.Token,
            stderrOverflow.Token);
        Process? process = null;
        Task<string>? stderrTask = null;
        try
        {
            process = Process.Start(CreateStartInfo())
                ?? throw new ArtifactReplayProcessException(
                    ReplayQuarantineCodes.WorkerFailure,
                    "无法启动归档 replay worker。",
                    systemicProtocolFailure: false);
            stderrTask = ReadStderrBoundedAsync(
                process.StandardError.BaseStream,
                _maximumStderrBytes,
                operation.Token);
            _ = stderrTask.ContinueWith(
                task =>
                {
                    if (!task.IsFaulted) return;
                    try { stderrOverflow.Cancel(); } catch (ObjectDisposedException) { }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            var hello = await ArtifactReplayProcessProtocol.ReadAsync<ArtifactReplayProcessHello>(
                process.StandardOutput.BaseStream,
                ArtifactReplayProcessProtocol.MaximumHelloBytes,
                operation.Token);
            if (!ArtifactReplayProcessProtocol.IsValidHello(
                    hello,
                    EngineArtifactId,
                    ArtifactFingerprint,
                    WorkerId,
                    _archive.Manifest.RuntimeIdentity.ManifestHash))
                throw Protocol("worker hello 与归档身份或完整指纹不一致。");

            await ArtifactReplayProcessProtocol.WriteAsync(
                process.StandardInput.BaseStream,
                requestBytes,
                operation.Token);
            process.StandardInput.Close();

            var server = await ArtifactReplayProcessProtocol.ReadAsync<ArtifactReplayProcessServerFrame>(
                process.StandardOutput.BaseStream,
                _maximumResponseBytes,
                operation.Token);
            if (!string.Equals(server.Schema, ArtifactReplayProcessProtocol.Schema, StringComparison.Ordinal))
                throw Protocol("worker 响应协议版本无效。");

            var trailing = new byte[1];
            var trailingCount = await process.StandardOutput.BaseStream.ReadAsync(
                trailing.AsMemory(),
                operation.Token);
            if (trailingCount != 0)
                throw Protocol("worker stdout 在唯一响应帧后包含额外消息或字节。");

            await process.WaitForExitAsync(operation.Token);
            var stderr = stderrTask is null ? string.Empty : await stderrTask;
            if (process.ExitCode != 0)
                throw new ArtifactReplayProcessException(
                    ReplayQuarantineCodes.WorkerFailure,
                    $"归档 replay worker 非零退出（exit={process.ExitCode}）：{TrimDetail(stderr)}",
                    systemicProtocolFailure: false);
            return server;
        }
        catch (OperationCanceledException ex)
        {
            if (process is not null) await TerminateProcessTreeAsync(process);
            var boundedFailure = await ObserveBoundedStderrFailureAsync(stderrTask);
            if (boundedFailure is not null)
                throw boundedFailure;
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new ArtifactReplayProcessException(
                ReplayQuarantineCodes.WorkerTimeout,
                $"归档 replay worker 超过 wall-clock 时限 {timeout.TotalMilliseconds:F0}ms，已终止整棵进程树。",
                systemicProtocolFailure: false,
                ex);
        }
        catch (ArtifactReplayProcessException)
        {
            if (process is not null) await TerminateProcessTreeAsync(process);
            _ = await CompleteStderrAsync(stderrTask);
            throw;
        }
        catch (Exception ex) when (ex is IOException
            or JsonException
            or InvalidDataException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            int? nonZeroExitCode = null;
            if (process is not null)
            {
                try
                {
                    if (process.HasExited && process.ExitCode != 0)
                        nonZeroExitCode = process.ExitCode;
                }
                catch (InvalidOperationException)
                {
                    // 进程尚未成功启动时按传输失败处理。
                }
            }
            if (process is not null) await TerminateProcessTreeAsync(process);
            var stderr = await CompleteStderrAsync(stderrTask);
            if (nonZeroExitCode is { } exitCode)
                throw new ArtifactReplayProcessException(
                    ReplayQuarantineCodes.WorkerFailure,
                    $"归档 replay worker 非零退出（exit={exitCode}）：{TrimDetail(stderr)}",
                    systemicProtocolFailure: false,
                    ex);
            throw new ArtifactReplayProcessException(
                ReplayQuarantineCodes.WorkerProtocolMismatch,
                $"归档 replay worker 传输失败：{ex.Message} {TrimDetail(stderr)}".Trim(),
                systemicProtocolFailure: true,
                ex);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private ProcessStartInfo CreateStartInfo()
    {
        if (_testStartInfoFactory is not null)
        {
            var fixture = _testStartInfoFactory()
                ?? throw new InvalidOperationException("测试进程工厂没有返回启动配置。");
            if (fixture.UseShellExecute
                || !fixture.RedirectStandardInput
                || !fixture.RedirectStandardOutput
                || !fixture.RedirectStandardError)
                throw new InvalidOperationException("测试进程必须使用与生产相同的三路重定向边界。");
            return fixture;
        }

        var entrypoint = _archive.Manifest.ReplayWorkerEntrypoint;
        var workingDirectory = ResolveArchivePath(entrypoint.WorkingDirectory!);
        var startInfo = new ProcessStartInfo
        {
            FileName = _dotnetExecutable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in entrypoint.Arguments)
            startInfo.ArgumentList.Add(argument);

        // 不继承数据库路径、密钥、代理和 ASP.NET 监听配置。该清理是应用层约束，
        // 不等同于 seccomp / namespace / Landlock 等 OS 沙箱。
        startInfo.Environment.Clear();
        startInfo.Environment["DOTNET_EnableDiagnostics"] = "0";
        startInfo.Environment["COMPlus_EnableDiagnostics"] = "0";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["GRANDUMI_REPLAY_WORKER_PROCESS"] = "1";
        startInfo.Environment["TZ"] = "UTC";
        startInfo.Environment["LANG"] = "C.UTF-8";
        if (OperatingSystem.IsWindows())
        {
            CopyRequiredWindowsEnvironment(startInfo, "SystemRoot");
            CopyRequiredWindowsEnvironment(startInfo, "WINDIR");
            CopyRequiredWindowsEnvironment(startInfo, "PATH");
        }
        else if (string.Equals(Environment.UserName, "root", StringComparison.Ordinal))
        {
            var restrictedUser = FindRestrictedUnixUser();
            if (restrictedUser is not null) startInfo.UserName = restrictedUser;
        }
        return startInfo;
    }

    private string ResolveArchivePath(string relative)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_archive.ArchiveDirectory));
        var resolved = Path.GetFullPath(Path.Combine(
            root,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, comparison)
            || !Directory.Exists(resolved))
            throw new ReplayArtifactArchiveException("worker 工作目录越过归档或不存在。");
        return resolved;
    }

    private static string ResolveTrustedDotnet(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("受信 dotnet 路径不能为空或包含首尾空白。", nameof(value));
        var fileName = Path.GetFileName(value);
        if (!string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("worker 只允许由固定 dotnet host 启动。", nameof(value));
        return value;
    }

    private static void CopyRequiredWindowsEnvironment(ProcessStartInfo startInfo, string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value)) startInfo.Environment[name] = value;
    }

    private static string? FindRestrictedUnixUser()
    {
        try
        {
            var users = File.ReadLines("/etc/passwd")
                .Select(line => line.Split(':', 2)[0])
                .ToHashSet(StringComparer.Ordinal);
            if (users.Contains("grandumi")) return "grandumi";
            if (users.Contains("nobody")) return "nobody";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 无法确认低权限账号时保持当前非沙箱边界，并由候选说明明确披露。
        }
        return null;
    }

    private static async Task<string> ReadStderrBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > maximumBytes)
                throw new ArtifactReplayProcessException(
                    ReplayQuarantineCodes.WorkerProtocolMismatch,
                    $"worker stderr 超过 {maximumBytes} 字节上限。",
                    systemicProtocolFailure: true);
            buffer.Write(chunk, 0, read);
        }
        return new UTF8Encoding(false, true).GetString(buffer.ToArray());
    }

    private static async Task TerminateProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException)
        {
            // 仍继续 WaitForExit；若进程已竞争退出，此处是正常路径。
        }

        try
        {
            using var terminationTimeout = new CancellationTokenSource(TerminationWaitTimeout);
            await process.WaitForExitAsync(terminationTimeout.Token);
        }
        catch (InvalidOperationException)
        {
            // Start 失败时没有可等待的进程。
        }
        catch (OperationCanceledException)
        {
            throw new ArtifactReplayProcessException(
                ReplayQuarantineCodes.WorkerTerminationFailed,
                $"终止 replay worker 后等待 {TerminationWaitTimeout.TotalSeconds:F0} 秒仍未退出；拒绝继续批量处理。",
                systemicProtocolFailure: true);
        }
    }

    private static string TrimDetail(string value)
    {
        var normalized = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length <= 1200
            ? normalized
            : "…" + normalized[^1199..];
    }

    private static async Task<string> CompleteStderrAsync(Task<string>? stderrTask)
    {
        if (stderrTask is null) return string.Empty;
        try
        {
            return await stderrTask;
        }
        catch (Exception ex)
        {
            return $"stderr 读取失败：{ex.GetBaseException().Message}";
        }
    }

    private static async Task<ArtifactReplayProcessException?> ObserveBoundedStderrFailureAsync(
        Task<string>? stderrTask)
    {
        if (stderrTask is null) return null;
        try
        {
            _ = await stderrTask;
            return null;
        }
        catch (ArtifactReplayProcessException ex)
        {
            return ex;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static ArtifactReplayProcessException Protocol(string message)
        => new(
            ReplayQuarantineCodes.WorkerProtocolMismatch,
            message,
            systemicProtocolFailure: true);
}

internal sealed record ArtifactReplayProcessHello(
    string Schema,
    string Kind,
    string ArtifactId,
    string ArtifactFingerprint,
    string WorkerId,
    string RuntimeManifestHash,
    string StableHash);

internal sealed record ArtifactReplayProcessClientFrame(
    string Schema,
    string Kind,
    ArtifactReplayWorkerRequest? Request);

internal sealed record ArtifactReplayProcessServerFrame(
    string Schema,
    string Kind,
    ArtifactReplayWorkerResponse? Response,
    ArtifactReplayWorkerProbe? Probe);

internal static class ArtifactReplayProcessProtocol
{
    public const string Schema = "grandumi.artifact_replay_process_protocol.v1";
    public const string HelloKind = "hello";
    public const string ProbeKind = "probe";
    public const string ReadyKind = "ready";
    public const string ExecuteKind = "execute";
    public const string ResultKind = "result";
    public const int MaximumHelloBytes = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 128,
    };

    public static string WorkerId(string artifactId) => $"artifact-process-{artifactId}";

    public static ArtifactReplayProcessHello CreateHello(
        string artifactId,
        string artifactFingerprint,
        string workerId,
        string runtimeManifestHash)
    {
        var withoutHash = new ArtifactReplayProcessHello(
            Schema,
            HelloKind,
            artifactId,
            artifactFingerprint,
            workerId,
            runtimeManifestHash,
            StableHash: string.Empty);
        return withoutHash with { StableHash = HashHello(withoutHash) };
    }

    public static ArtifactReplayWorkerProbe CreateProbe(ArtifactReplayProcessHello hello)
    {
        var canonical = JsonSerializer.SerializeToElement(new
        {
            hello.ArtifactId,
            hello.ArtifactFingerprint,
            hello.WorkerId,
            hello.RuntimeManifestHash,
            hello.StableHash,
        });
        return new ArtifactReplayWorkerProbe(
            hello.ArtifactId,
            hello.ArtifactFingerprint,
            hello.WorkerId,
            hello.RuntimeManifestHash,
            CanonicalJson.Hash(canonical));
    }

    public static bool IsValidHello(
        ArtifactReplayProcessHello hello,
        string artifactId,
        string artifactFingerprint,
        string workerId,
        string runtimeManifestHash)
        => string.Equals(hello.Schema, Schema, StringComparison.Ordinal)
            && string.Equals(hello.Kind, HelloKind, StringComparison.Ordinal)
            && string.Equals(hello.ArtifactId, artifactId, StringComparison.Ordinal)
            && string.Equals(hello.ArtifactFingerprint, artifactFingerprint, StringComparison.Ordinal)
            && string.Equals(hello.WorkerId, workerId, StringComparison.Ordinal)
            && string.Equals(hello.RuntimeManifestHash, runtimeManifestHash, StringComparison.Ordinal)
            && string.Equals(hello.StableHash, HashHello(hello), StringComparison.Ordinal);

    public static bool IsValidProbe(
        ArtifactReplayWorkerProbe probe,
        string artifactId,
        string artifactFingerprint,
        string workerId,
        string runtimeManifestHash)
    {
        var expected = CreateProbe(CreateHello(
            artifactId,
            artifactFingerprint,
            workerId,
            runtimeManifestHash));
        return probe == expected;
    }

    public static byte[] Serialize<T>(T value)
        => CanonicalJson.Encode(JsonSerializer.SerializeToElement(value, JsonOptions));

    public static async Task WriteAsync(
        Stream stream,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix.AsMemory(), cancellationToken);
        await stream.WriteAsync(payload.AsMemory(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T> ReadAsync<T>(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var prefix = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, prefix, cancellationToken);
        var length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length <= 0 || length > maximumBytes)
            throw new InvalidDataException(
                $"worker 协议帧长度 {length} 超出 1..{maximumBytes}。");
        var bytes = new byte[length];
        await ReadExactlyAsync(stream, bytes, cancellationToken);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = JsonOptions.MaxDepth,
        });
        var canonical = CanonicalJson.Encode(document.RootElement);
        if (!bytes.AsSpan().SequenceEqual(canonical))
            throw new InvalidDataException("worker 协议帧不是唯一规范 JSON 字节编码。");
        _ = StrictUtf8.GetString(bytes);
        return document.RootElement.Deserialize<T>(JsonOptions)
            ?? throw new InvalidDataException("worker 协议帧内容为空。");
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("worker 协议帧被截断。");
            offset += read;
        }
    }

    private static string HashHello(ArtifactReplayProcessHello hello)
        => CanonicalJson.Hash(JsonSerializer.SerializeToElement(new
        {
            hello.Schema,
            hello.Kind,
            hello.ArtifactId,
            hello.ArtifactFingerprint,
            hello.WorkerId,
            hello.RuntimeManifestHash,
        }));
}

/// <summary>由归档 payload/publish 内历史 GrandUMIServer.dll 执行的单次 worker host。</summary>
internal static class ArtifactReplayProcessHost
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var protocolOutput = Console.OpenStandardOutput();
        var stderrWriter = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
        Console.SetOut(stderrWriter);
        Console.SetError(stderrWriter);
        try
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable("GRANDUMI_REPLAY_WORKER_PROCESS"),
                    "1",
                    StringComparison.Ordinal))
                throw new ReplayArtifactArchiveException("worker host 缺少受控进程环境标记。");

            var archiveDirectory = ResolveArchiveDirectoryFromRuntimeBase(AppContext.BaseDirectory);
            var archive = ReplayArtifactArchive.Verify(archiveDirectory);
            var identity = ReplayArtifactCommand.VerifyArchivedRuntimeInCurrentProcess(archive);
            var descriptor = ReplayArtifactArchive.CreateTestDescriptor(archive.Manifest);
            var workerId = ArtifactReplayProcessProtocol.WorkerId(descriptor.EngineArtifactId);
            var worker = new InProcessArtifactReplayWorker(
                workerId,
                descriptor,
                DeterministicReplayCheckpointProvider.Current,
                GrandUMI.Effects.Rules.CardRulesetManager.Current);
            VerifyArchiveRemainsImmutable(archive, "worker_constructed");
            var hello = ArtifactReplayProcessProtocol.CreateHello(
                descriptor.EngineArtifactId,
                ReplayArtifactIdentity.Fingerprint(descriptor),
                workerId,
                identity.ManifestHash);
            await ArtifactReplayProcessProtocol.WriteAsync(
                protocolOutput,
                ArtifactReplayProcessProtocol.Serialize(hello),
                cancellationToken);

            var protocolInput = Console.OpenStandardInput();
            var client = await ArtifactReplayProcessProtocol.ReadAsync<ArtifactReplayProcessClientFrame>(
                protocolInput,
                ProcessArtifactReplayWorker.DefaultMaximumRequestBytes,
                cancellationToken);
            var trailing = new byte[1];
            if (await protocolInput.ReadAsync(trailing.AsMemory(), cancellationToken) != 0)
                throw new InvalidDataException("worker 请求后存在重复帧或额外字节。");
            if (!string.Equals(client.Schema, ArtifactReplayProcessProtocol.Schema, StringComparison.Ordinal))
                throw new InvalidDataException("worker 客户端协议版本无效。");

            ArtifactReplayProcessServerFrame server;
            if (string.Equals(client.Kind, ArtifactReplayProcessProtocol.ProbeKind, StringComparison.Ordinal)
                && client.Request is null)
            {
                server = new ArtifactReplayProcessServerFrame(
                    ArtifactReplayProcessProtocol.Schema,
                    ArtifactReplayProcessProtocol.ReadyKind,
                    Response: null,
                    ArtifactReplayProcessProtocol.CreateProbe(hello));
            }
            else if (string.Equals(client.Kind, ArtifactReplayProcessProtocol.ExecuteKind, StringComparison.Ordinal)
                && client.Request is not null)
            {
                var response = await worker.ExecuteAsync(client.Request, cancellationToken);
                server = new ArtifactReplayProcessServerFrame(
                    ArtifactReplayProcessProtocol.Schema,
                    ArtifactReplayProcessProtocol.ResultKind,
                    response,
                    Probe: null);
            }
            else
            {
                throw new InvalidDataException("worker 客户端 kind/payload 互斥关系无效。");
            }

            VerifyArchiveRemainsImmutable(archive, "response_ready");
            var responseBytes = ArtifactReplayProcessProtocol.Serialize(server);
            if (responseBytes.Length > ProcessArtifactReplayWorker.DefaultMaximumResponseBytes)
                throw new InvalidDataException("worker 响应超过协议大小上限。");
            await ArtifactReplayProcessProtocol.WriteAsync(
                protocolOutput,
                responseBytes,
                cancellationToken);
            protocolOutput.Close();
            return 0;
        }
        catch (Exception ex) when (ex is ReplayArtifactArchiveException
            or ReplayArtifactRegistryException
            or ArtifactReplayProcessException
            or InvalidDataException
            or JsonException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            Console.Error.WriteLine($"归档 replay worker host 失败：{ex.Message}");
            try { protocolOutput.Close(); } catch { }
            return 1;
        }
    }

    internal static string ResolveArchiveDirectoryFromRuntimeBase(string runtimeBaseDirectory)
    {
        var publishRoot = ReplayArtifactCommand.NormalizeRuntimeBindingPath(runtimeBaseDirectory);
        return Path.GetFullPath(Path.Combine(publishRoot, "..", ".."));
    }

    private static void VerifyArchiveRemainsImmutable(
        VerifiedReplayArtifactArchive archive,
        string phase)
    {
        try
        {
            _ = ReplayArtifactArchive.Verify(archive.ManifestPath);
        }
        catch (ReplayArtifactArchiveException ex)
        {
            throw new ReplayArtifactArchiveException(
                $"worker host 在 {phase} 阶段检测到归档内容变化：{ex.Message}",
                ex);
        }
    }
}
