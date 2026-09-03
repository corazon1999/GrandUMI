using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;

namespace GrandUMI.Game;

/// <summary>
/// 对局恢复检查点。引擎仍以确定性动作重放恢复（部分效果含异步续延，不能直接反序列化），
/// 检查点用于保存最近的完整私有状态、幂等窗口和操作序号，并校验重放结果。
/// </summary>
internal static class RoomRecoverySnapshotStore
{
    // v7：私有状态哈希纳入海克斯扩展运行态、卡牌永久实体费用及登场来源标记。
    // v6：私有状态哈希纳入效果执行序号及已消费的选择性触发无效执行键。
    // v5：私有状态哈希纳入海克斯逐槽刷新记录及双方独立的整局候选出现历史。
    // v4：私有状态哈希纳入海克斯随机子授予计划及“超凡邪恶”跨回合累计力量。
    // v3：私有状态哈希纳入场上卡的“建立时快照”来源 ID。
    // v2：私有状态哈希纳入服务端权威贴咚撤回序号与栈。
    // v1-v6 仍须读取其请求去重窗口；只跳过旧结构的状态哈希比对，并在重放后刷新为 v7。
    // 若整份忽略旧版，升级重启后的迟到重试可能再次执行已接受的贴咚等非幂等动作。
    internal const int MinimumCompatibleSchemaVersion = 1;
    internal const int SchemaVersion = 7;
    internal const int CaptureEveryAcceptedActions = 16;
    private static readonly Channel<SnapshotCommand> Queue = Channel.CreateBounded<SnapshotCommand>(
        new BoundedChannelOptions(2_048)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    private static readonly Task Worker = Task.Run(ProcessAsync);
    private static int _stopped;
    private static int _queueDepth;
    private static long _writeFailures;
    private static long _lastFailureUtcTicks;

    /// <summary>仅供故障演练测试注入；生产代码不得设置。</summary>
    internal static Func<string, Exception?>? WriteFailureInjector { get; set; }

    internal static void Capture(RoomRecoverySnapshot snapshot)
    {
        if (Volatile.Read(ref _stopped) != 0)
            throw new InvalidOperationException("恢复快照队列已停止");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EnqueueRequiredAsync(
            new SnapshotCommand(snapshot.RoomId, Serialize(snapshot), Delete: false, completion),
            completion).GetAwaiter().GetResult();
    }

    internal static Task DeleteDeferred(string roomId)
    {
        if (Volatile.Read(ref _stopped) != 0) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return EnqueueRequiredAsync(new SnapshotCommand(roomId, null, Delete: true, completion), completion);
    }

    internal static Task FlushAsync()
    {
        if (Volatile.Read(ref _stopped) != 0) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return EnqueueRequiredAsync(new SnapshotCommand(string.Empty, null, Delete: false, completion), completion);
    }

    internal static RoomRecoverySnapshot? TryRead(string roomId)
    {
        try
        {
            var path = PathOf(roomId);
            if (!File.Exists(path)) return null;
            var snapshot = JsonSerializer.Deserialize<RoomRecoverySnapshot>(File.ReadAllBytes(path));
            return snapshot is not null
                   && snapshot.SchemaVersion is >= MinimumCompatibleSchemaVersion and <= SchemaVersion
                   && snapshot.RoomId == roomId
                ? snapshot
                : null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[恢复快照] 读取 {roomId} 失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>恢复路径严格读取：文件存在却损坏或版本不兼容时必须隔离房间。</summary>
    internal static RoomRecoverySnapshot? ReadRequiredIfExists(string roomId)
    {
        var path = PathOf(roomId);
        if (!File.Exists(path)) return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize<RoomRecoverySnapshot>(File.ReadAllBytes(path))
                ?? throw new InvalidDataException("恢复快照内容为空");
            if (snapshot.SchemaVersion is < MinimumCompatibleSchemaVersion or > SchemaVersion)
                throw new InvalidDataException($"恢复快照版本 {snapshot.SchemaVersion} 不兼容");
            if (!string.Equals(snapshot.RoomId, roomId, StringComparison.Ordinal))
                throw new InvalidDataException("恢复快照房间标识不一致");
            return snapshot;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"恢复快照读取失败：{ex.Message}", ex);
        }
    }

    internal static string ComputeStateSha256(JsonElement state)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state))).ToLowerInvariant();

    internal static void Shutdown()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        Queue.Writer.TryComplete();
        Worker.GetAwaiter().GetResult();
    }

    internal static int QueueDepth => Math.Max(0, Volatile.Read(ref _queueDepth));
    internal static long WriteFailures => Interlocked.Read(ref _writeFailures);
    internal static DateTime? LastFailureUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastFailureUtcTicks);
            return ticks <= 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    private static byte[] Serialize(RoomRecoverySnapshot snapshot)
        => JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions { WriteIndented = false });

    private static string PathOf(string roomId)
        => Path.Combine(RoomJournal.GetPersistDir(), $"{roomId}.snapshot.json");

    private static async Task EnqueueRequiredAsync(
        SnapshotCommand command,
        TaskCompletionSource completion)
    {
        var queued = false;
        try
        {
            Interlocked.Increment(ref _queueDepth);
            await Queue.Writer.WriteAsync(command);
            queued = true;
            await completion.Task;
        }
        catch (Exception ex)
        {
            if (!queued) Interlocked.Decrement(ref _queueDepth);
            completion.TrySetException(ex);
            throw;
        }
    }

    private static async Task ProcessAsync()
    {
        await foreach (var command in Queue.Reader.ReadAllAsync())
        {
            Interlocked.Decrement(ref _queueDepth);
            try
            {
                if (!string.IsNullOrEmpty(command.RoomId)
                    && WriteFailureInjector?.Invoke(command.RoomId) is { } injected)
                    throw injected;
                if (command.Payload is null && !command.Delete)
                {
                    command.Completion?.TrySetResult();
                    continue;
                }
                var path = PathOf(command.RoomId);
                if (command.Delete)
                {
                    File.Delete(path);
                }
                else if (command.Payload is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var temporary = path + ".tmp";
                    await File.WriteAllBytesAsync(temporary, command.Payload);
                    File.Move(temporary, path, overwrite: true);
                }
                command.Completion?.TrySetResult();
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _writeFailures);
                Interlocked.Exchange(ref _lastFailureUtcTicks, DateTime.UtcNow.Ticks);
                command.Completion?.TrySetException(ex);
                Console.Error.WriteLine($"[恢复快照] 写入 {command.RoomId} 失败：{ex.Message}");
            }
        }
    }

    private sealed record SnapshotCommand(
        string RoomId,
        byte[]? Payload,
        bool Delete,
        TaskCompletionSource? Completion = null);
}

internal sealed record RoomRecoverySnapshot(
    int SchemaVersion,
    string RoomId,
    long JournalSequence,
    DateTime CapturedAtUtc,
    long[] LastOperationSequences,
    long[] OperationClockRemainingMs,
    RequestDedupeEntry[] ProcessedRequests,
    string StateSha256,
    JsonElement PrivateState);
