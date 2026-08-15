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
    internal const int SchemaVersion = 1;
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

    internal static void Capture(RoomRecoverySnapshot snapshot)
    {
        if (Volatile.Read(ref _stopped) != 0) return;
        Queue.Writer.TryWrite(new SnapshotCommand(snapshot.RoomId, Serialize(snapshot), Delete: false));
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
            return snapshot is { SchemaVersion: SchemaVersion } && snapshot.RoomId == roomId
                ? snapshot
                : null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[恢复快照] 读取 {roomId} 失败：{ex.Message}");
            return null;
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

    private static byte[] Serialize(RoomRecoverySnapshot snapshot)
        => JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions { WriteIndented = false });

    private static string PathOf(string roomId)
        => Path.Combine(RoomJournal.GetPersistDir(), $"{roomId}.snapshot.json");

    private static async Task EnqueueRequiredAsync(
        SnapshotCommand command,
        TaskCompletionSource completion)
    {
        try
        {
            await Queue.Writer.WriteAsync(command);
            await completion.Task;
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            throw;
        }
    }

    private static async Task ProcessAsync()
    {
        await foreach (var command in Queue.Reader.ReadAllAsync())
        {
            try
            {
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
