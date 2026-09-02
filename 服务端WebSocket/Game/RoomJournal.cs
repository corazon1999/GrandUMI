using System.Text.Json;
using GrandUMI.Game.Logging;

namespace GrandUMI.Game;

/// <summary>
/// 房间动作日志（重启恢复用）：每个 PvP 房间一份 <c>Persist/&lt;roomId&gt;.jsonl</c>，
/// 记录"重放重建所需的最小信息" —— 首行 header（seed/牌组/先手等）+ 之后每个被接受动作一行。
///
/// 设计要点：
/// - 扁平存放（不按天分目录），便于服务器启动时一次扫描 Persist/ 目录恢复未结束的对局。
/// - 分胜负/房间结束时删除该文件（<see cref="Delete"/>），避免恢复已结束的局。
/// - 仅持久化"被接受"的动作（拒绝的不写），重放时才不会引入分歧。
/// - 每条 action 带 tsUtc 时间戳，供恢复时按"自最后一次操作起 30 分钟"做 TTL 判定。
/// 与普通 MatchLog 不同，本日志是恢复事务的一部分：创建和被接受动作必须完成物理刷新后才可确认。
/// 队列暂满会施加背压；打开、入队、序列化或写盘失败必须向房间协调器抛出。
/// </summary>
public static class RoomJournal
{
    private static readonly AsyncJsonlWriter Writer = new(jsonOptions: null, capacity: 8_192);
    private static long _durableCommitFailures;
    private static long _tailTruncations;
    private static long _lastFailureUtcTicks;

    /// <summary>仅供故障演练测试注入；生产代码不得设置。</summary>
    internal static Func<string, string, Exception?>? DurableFailureInjector { get; set; }

    /// <summary>持久化根目录：服务端项目根下的 Persist/（向上查找 GrandUMIServer.csproj）。</summary>
    public static string GetPersistDir()
    {
        var configured = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var dataDir = Environment.GetEnvironmentVariable("GRANDUMI_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dataDir)) return Path.GetFullPath(Path.Combine(dataDir, "Persist"));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GrandUMIServer.csproj")))
                return Path.Combine(dir.FullName, "Persist");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "Persist");
    }

    internal static string PathOf(string roomId)
        => Path.Combine(GetPersistDir(), $"{roomId}.jsonl");

    /// <summary>建文件并写入 header（首行 {kind:"create", ...}）。append:false 覆盖旧残留。</summary>
    public static void Open(string roomId, object header)
    {
        ExecuteDurable(roomId, "open", () =>
            Writer.OpenAndAppendDurable(roomId, PathOf(roomId), append: false, header));
    }

    /// <summary>以追加模式重开已有日志（重启恢复后续写新动作，不重写 header）。</summary>
    public static void Reopen(string roomId)
    {
        ExecuteDurable(roomId, "reopen", () =>
            Writer.OpenRequired(roomId, PathOf(roomId), append: true));
    }

    /// <summary>追加一个被接受的动作（{kind:"action", playerIndex, action, data, tsUtc}）。</summary>
    public static void Append(
        string roomId,
        long journalSequence,
        int playerIndex,
        string action,
        JsonElement data,
        string? requestId = null,
        long? operationSequence = null,
        GameActionSource source = GameActionSource.Player,
        string? hexDraftRoundId = null,
        DateTime? hexDraftDeadlineUtc = null)
    {
        ExecuteDurable(roomId, "action", () =>
        {
            Writer.AppendDurable(roomId, new
            {
                kind = "action",
                journalSequence,
                playerIndex,
                action,
                data,
                requestId,
                operationSequence,
                source = GameActionSourceWire.Value(source),
                hexDraftRoundId,
                hexDraftDeadlineUtc,
                tsUtc = DateTime.UtcNow,
            });
        });
    }

    /// <summary>保存最近一次正式操作后的双方剩余棋钟；服务重启期间不继续扣时。</summary>
    public static void AppendClock(
        string roomId,
        IReadOnlyList<long> remainingMs,
        IReadOnlyList<long> turnRemainingMs,
        int turnCount,
        IReadOnlyList<bool> turnExtensionUsed)
    {
        if (remainingMs.Count < 2
            || turnRemainingMs.Count < 2
            || turnExtensionUsed.Count < 2)
            return;
        ExecuteDurable(roomId, "clock", () =>
        {
            Writer.AppendDurable(roomId, new
            {
                kind = "clock",
                player0RemainingMs = remainingMs[0],
                player1RemainingMs = remainingMs[1],
                player0TurnRemainingMs = turnRemainingMs[0],
                player1TurnRemainingMs = turnRemainingMs[1],
                turnCount,
                player0TurnExtensionUsed = turnExtensionUsed[0],
                player1TurnExtensionUsed = turnExtensionUsed[1],
                tsUtc = DateTime.UtcNow,
            });
        });
    }

    /// <summary>
    /// 追加服务端裁定的终局意图。它与动作共用连续序号，并在修改内存胜负状态、广播和清房之前
    /// 完成物理刷新；进程在任意后续步骤退出时，启动恢复都能重放同一个终局并继续幂等结算。
    /// </summary>
    public static void AppendTerminal(
        string roomId,
        long journalSequence,
        int winnerIndex,
        int expiredPlayer,
        string terminalKind,
        string reason,
        DateTime completedAtUtc)
    {
        if (winnerIndex is not (0 or 1) || expiredPlayer is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(winnerIndex), "终局玩家编号非法");
        ExecuteDurable(roomId, "terminal", () =>
        {
            Writer.AppendDurable(roomId, new
            {
                kind = "terminal",
                journalSequence,
                winnerIndex,
                expiredPlayer,
                terminalKind,
                reason,
                completedAtUtc = completedAtUtc.ToUniversalTime(),
                tsUtc = completedAtUtc.ToUniversalTime(),
            });
        });
    }

    /// <summary>
    /// 读取已提交的换行边界。进程在写入中途被终止时，最后一个不完整行从未得到 fsync 确认，
    /// 可以安全截断；已换行但 JSON/序号损坏则由上层按数据损坏隔离，不能静默忽略。
    /// </summary>
    internal static async Task<CommittedJournalRead> ReadCommittedLinesAsync(string path)
    {
        byte[] bytes;
        await using (var readStream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan | FileOptions.Asynchronous))
        {
            if (readStream.Length > int.MaxValue)
                throw new InvalidDataException("恢复日志超过单房间读取上限");
            bytes = new byte[(int)readStream.Length];
            await readStream.ReadExactlyAsync(bytes);
        }
        if (bytes.Length == 0) return new CommittedJournalRead(Array.Empty<string>(), false);

        var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
        if (lastNewline < 0)
            throw new InvalidDataException("恢复日志没有任何已提交的换行记录");

        var hadIncompleteTail = lastNewline != bytes.Length - 1;
        if (hadIncompleteTail)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.None);
            stream.SetLength(lastNewline + 1L);
            stream.Flush(flushToDisk: true);
            Interlocked.Increment(ref _tailTruncations);
        }

        var text = new System.Text.UTF8Encoding(false, true).GetString(bytes, 0, lastNewline + 1);
        var lines = text.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .ToArray();
        return new CommittedJournalRead(lines, hadIncompleteTail);
    }

    /// <summary>关闭并删除该房间的日志（分胜负/结束时调用）。</summary>
    public static void Delete(string roomId)
        => DeleteDeferred(roomId).GetAwaiter().GetResult();

    public static Task DeleteDeferred(string roomId)
    {
        try { return Writer.DeleteDeferred(roomId, PathOf(roomId)); }
        catch { return Task.CompletedTask; }
    }

    public static int QueueDepth => Writer.QueueDepth;
    public static long DroppedEntries => Writer.DroppedEntries;
    public static long DurableCommitFailures => Interlocked.Read(ref _durableCommitFailures);
    public static long TailTruncations => Interlocked.Read(ref _tailTruncations);
    public static DateTime? LastFailureUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastFailureUtcTicks);
            return ticks <= 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }
    public static void Shutdown() => Writer.Shutdown();

    private static void ExecuteDurable(string roomId, string operation, Action action)
    {
        try
        {
            if (DurableFailureInjector?.Invoke(roomId, operation) is { } injected)
                throw injected;
            action();
        }
        catch
        {
            Interlocked.Increment(ref _durableCommitFailures);
            Interlocked.Exchange(ref _lastFailureUtcTicks, DateTime.UtcNow.Ticks);
            throw;
        }
    }

}

internal sealed record CommittedJournalRead(IReadOnlyList<string> Lines, bool HadIncompleteTail);
