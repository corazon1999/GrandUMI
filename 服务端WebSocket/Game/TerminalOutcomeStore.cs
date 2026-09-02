using System.Text.Json;
using GrandUMI.Game.Snapshot;

namespace GrandUMI.Game;

/// <summary>
/// 已结束对局的短期权威快照。终局广播可能恰好落在断线窗口内，因此房间移除前先把双方
/// 视角完整落盘；同账号恢复登录或旧会话补拉状态时，可以重发同一份幂等终局快照。
/// </summary>
internal static class TerminalOutcomeStore
{
    internal const int SchemaVersion = 1;
    internal static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>仅供故障演练测试注入；生产代码不得设置。</summary>
    internal static Func<string, Exception?>? WriteFailureInjector { get; set; }

    internal static string GetDirectory()
        => Path.Combine(RoomJournal.GetPersistDir(), "TerminalOutcomes");

    internal static string PathOf(string roomId)
    {
        if (!IsSafeRoomId(roomId)) throw new InvalidDataException("终局快照房间 ID 非法");
        return Path.Combine(GetDirectory(), $"{roomId}.json");
    }

    internal static TerminalOutcomeRecord Save(
        string roomId,
        DateTime completedAtUtc,
        MatchKind matchKind,
        IReadOnlyList<string> accounts,
        IReadOnlyList<string> sessionIds,
        GameState state,
        Func<int, object?>? cinematicProvider = null)
    {
        if (accounts.Count < 2 || sessionIds.Count < 2)
            throw new ArgumentException("终局快照缺少双方身份");
        if (!state.IsGameOver)
            throw new InvalidOperationException("非终局状态不得写入终局快照");
        if (WriteFailureInjector?.Invoke(roomId) is { } injected) throw injected;

        var normalizedCompletedAt = completedAtUtc.ToUniversalTime();
        var record = new TerminalOutcomeRecord(
            SchemaVersion,
            roomId,
            normalizedCompletedAt,
            matchKind.ToString(),
            state.WinnerIndex,
            state.IsDraw,
            state.GameOverReason ?? "",
            [accounts[0], accounts[1]],
            [sessionIds[0], sessionIds[1]],
            [
                JsonSerializer.SerializeToElement(
                    StateSnapshotBuilder.Build(
                        state,
                        0,
                        "TerminalRecovery",
                        cinematic: cinematicProvider?.Invoke(0)), JsonOptions),
                JsonSerializer.SerializeToElement(
                    StateSnapshotBuilder.Build(
                        state,
                        1,
                        "TerminalRecovery",
                        cinematic: cinematicProvider?.Invoke(1)), JsonOptions),
            ]);

        lock (Gate)
        {
            Directory.CreateDirectory(GetDirectory());
            var path = PathOf(roomId);
            if (File.Exists(path))
            {
                var existing = ReadRequired(path);
                EnsureSameOutcome(existing, record);
                return existing;
            }

            var temporaryPath = Path.Combine(
                GetDirectory(),
                $".{roomId}.{Guid.NewGuid():N}.tmp");
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           bufferSize: 16 * 1024,
                           FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                try
                {
                    File.Move(temporaryPath, path, overwrite: false);
                }
                catch (IOException) when (File.Exists(path))
                {
                    var existing = ReadRequired(path);
                    EnsureSameOutcome(existing, record);
                    return existing;
                }
                return record;
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }
        }
    }

    internal static bool TryGetBySession(string sessionId, out JsonElement snapshot)
        => TryFind(
            record => Array.FindIndex(record.SessionIds,
                candidate => string.Equals(candidate, sessionId, StringComparison.Ordinal)),
            out snapshot);

    internal static bool TryGetByAccount(string account, out JsonElement snapshot)
        => TryFind(
            record => Array.FindIndex(record.Accounts,
                candidate => string.Equals(candidate, account, StringComparison.OrdinalIgnoreCase)),
            out snapshot);

    /// <summary>账号进入新对局后，旧局终局不再是可恢复状态，避免恢复登录误取过期对局。</summary>
    internal static void DeleteForAccounts(IEnumerable<string> accounts)
    {
        var normalized = accounts
            .Where(account => !string.IsNullOrWhiteSpace(account))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalized.Count == 0) return;

        lock (Gate)
        {
            var directory = GetDirectory();
            if (!Directory.Exists(directory)) return;
            foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var record = ReadRequired(path);
                    if (record.Accounts.Any(normalized.Contains)) TryDeleteFile(path);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[终局快照] 清理 {Path.GetFileName(path)} 失败：{ex.Message}");
                }
            }
        }
    }

    private static bool TryFind(Func<TerminalOutcomeRecord, int> selectPlayer, out JsonElement snapshot)
    {
        snapshot = default;
        lock (Gate)
        {
            var directory = GetDirectory();
            if (!Directory.Exists(directory)) return false;
            var now = DateTime.UtcNow;
            TerminalOutcomeRecord? selected = null;
            var selectedIndex = -1;
            foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var record = ReadRequired(path);
                    if (now - record.CompletedAtUtc.ToUniversalTime() > Retention)
                    {
                        TryDeleteFile(path);
                        continue;
                    }
                    var playerIndex = selectPlayer(record);
                    if (playerIndex is not (0 or 1)) continue;
                    if (selected is not null && selected.CompletedAtUtc >= record.CompletedAtUtc) continue;
                    selected = record;
                    selectedIndex = playerIndex;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[终局快照] 读取 {Path.GetFileName(path)} 失败：{ex.Message}");
                }
            }
            if (selected is null || selectedIndex is not (0 or 1)) return false;
            snapshot = selected.PlayerSnapshots[selectedIndex].Clone();
            return true;
        }
    }

    private static TerminalOutcomeRecord ReadRequired(string path)
    {
        var record = JsonSerializer.Deserialize<TerminalOutcomeRecord>(File.ReadAllBytes(path), JsonOptions)
            ?? throw new InvalidDataException("终局快照内容为空");
        if (record.SchemaVersion != SchemaVersion
            || !IsSafeRoomId(record.RoomId)
            || record.Accounts.Length != 2
            || record.SessionIds.Length != 2
            || record.PlayerSnapshots.Length != 2)
            throw new InvalidDataException("终局快照结构无效");
        return record;
    }

    private static void EnsureSameOutcome(TerminalOutcomeRecord existing, TerminalOutcomeRecord current)
    {
        if (!string.Equals(existing.RoomId, current.RoomId, StringComparison.Ordinal)
            || existing.WinnerIndex != current.WinnerIndex
            || existing.IsDraw != current.IsDraw
            || !string.Equals(existing.Reason, current.Reason, StringComparison.Ordinal)
            || !existing.Accounts.SequenceEqual(current.Accounts, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"房间 {current.RoomId} 的终局快照与既有权威结果冲突");
    }

    private static bool IsSafeRoomId(string roomId)
        => roomId.Length is >= 1 and <= 64
           && roomId.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 到期清理是尽力而为；写入的权威快照本身不会被覆盖。
        }
    }
}

internal sealed record TerminalOutcomeRecord(
    int SchemaVersion,
    string RoomId,
    DateTime CompletedAtUtc,
    string MatchKind,
    int? WinnerIndex,
    bool IsDraw,
    string Reason,
    string[] Accounts,
    string[] SessionIds,
    JsonElement[] PlayerSnapshots);
