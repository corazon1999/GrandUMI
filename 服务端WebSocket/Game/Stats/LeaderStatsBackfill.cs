using System.Text.Json;

namespace GrandUMI.Game.Stats;

public sealed record LeaderStatsBackfillReport(
    int FilesScanned,
    int AlreadyRecorded,
    int Imported,
    int SkippedIncomplete,
    int SkippedInvalid,
    IReadOnlyList<string> Errors);

/// <summary>从正式对局 JSONL 日志中幂等回填 Leader 排行榜事实。</summary>
public static class LeaderStatsBackfill
{
    public static LeaderStatsBackfillReport ImportDirectory(string logDirectory, LeaderStatsStore store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentNullException.ThrowIfNull(store);

        var fullLogDirectory = Path.GetFullPath(logDirectory);
        if (!Directory.Exists(fullLogDirectory))
            throw new DirectoryNotFoundException($"对局日志目录不存在：{fullLogDirectory}");

        var filesScanned = 0;
        var alreadyRecorded = 0;
        var imported = 0;
        var skippedIncomplete = 0;
        var skippedInvalid = 0;
        var errors = new List<string>();

        foreach (var file in Directory.EnumerateFiles(fullLogDirectory, "*.jsonl", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            filesScanned++;
            var fileMatchId = Path.GetFileNameWithoutExtension(file);

            try
            {
                if (store.ContainsMatch(fileMatchId))
                {
                    alreadyRecorded++;
                    continue;
                }

                var parsed = ParseFile(file, fileMatchId);
                if (!parsed.Completed)
                {
                    skippedIncomplete++;
                    continue;
                }

                if (parsed.Match is null)
                {
                    skippedInvalid++;
                    continue;
                }

                if (store.RecordMatch(parsed.Match)) imported++;
                else alreadyRecorded++;
            }
            catch (Exception ex)
            {
                errors.Add($"{fileMatchId}: {ex.Message}");
            }
        }

        return new LeaderStatsBackfillReport(
            filesScanned,
            alreadyRecorded,
            imported,
            skippedIncomplete,
            skippedInvalid,
            errors);
    }

    private static ParsedMatchLog ParseFile(string file, string fallbackMatchId)
    {
        var matchId = fallbackMatchId;
        var startSeen = false;
        var completed = false;
        var player0Account = "";
        var player1Account = "";
        var player0Leader = "";
        var player1Leader = "";
        var firstPlayer = -1;
        var winnerIndex = (int?)null;
        var turnCount = 0;
        var finishReason = "";
        var endedAtUtc = default(DateTime);
        var matchKind = MatchKind.Matchmaking;

        foreach (var line in File.ReadLines(file))
        {
            var needsFirstPlayer = firstPlayer is not (0 or 1);
            var isCandidate = line.Contains("\"kind\":\"match_start\"", StringComparison.Ordinal)
                              || line.Contains("\"kind\":\"match_end\"", StringComparison.Ordinal)
                              || (needsFirstPlayer &&
                                  (line.Contains("\"kind\":\"public_snapshot\"", StringComparison.Ordinal)
                                   || line.Contains("\"kind\":\"private_snapshot\"", StringComparison.Ordinal)
                                   || line.Contains("ChooseFirstPlayer", StringComparison.Ordinal)));
            if (!isCandidate) continue;

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var kind = ReadString(root, "kind");
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                continue;

            if (kind == "match_start")
            {
                startSeen = true;
                matchId = ReadString(root, "matchId") ?? fallbackMatchId;
                ReadPlayers(payload, ref player0Account, ref player1Account, ref player0Leader, ref player1Leader);
                firstPlayer = ReadInt(payload, "firstPlayer") ?? firstPlayer;
                matchKind = ReadMatchKind(payload, matchKind);
                continue;
            }

            if (needsFirstPlayer)
                firstPlayer = ReadFirstPlayer(kind, root, payload) ?? firstPlayer;

            if (kind != "match_end") continue;

            completed = true;
            winnerIndex = ReadInt(payload, "winnerIndex");
            turnCount = ReadInt(payload, "turnCount") ?? 0;
            finishReason = ReadString(payload, "reason") ?? "";
            matchKind = ReadMatchKind(payload, matchKind);
            if (root.TryGetProperty("timeUtc", out var timeElement) && timeElement.TryGetDateTime(out var parsedTime))
                endedAtUtc = parsedTime.Kind == DateTimeKind.Utc ? parsedTime : parsedTime.ToUniversalTime();
            break;
        }

        if (!completed) return new ParsedMatchLog(null, false);
        if (!startSeen || endedAtUtc == default || firstPlayer is not (0 or 1)
            || string.IsNullOrWhiteSpace(matchId)
            || string.IsNullOrWhiteSpace(player0Account) || string.IsNullOrWhiteSpace(player1Account)
            || string.IsNullOrWhiteSpace(player0Leader) || string.IsNullOrWhiteSpace(player1Leader))
            return new ParsedMatchLog(null, true);

        if (string.Equals(player0Account, "测试机器人", StringComparison.Ordinal)
            || string.Equals(player1Account, "测试机器人", StringComparison.Ordinal))
            matchKind = MatchKind.Bot;

        return new ParsedMatchLog(
            new LeaderMatchResult(
                matchId,
                endedAtUtc,
                matchKind,
                player0Account,
                player1Account,
                player0Leader,
                player1Leader,
                winnerIndex,
                firstPlayer,
                turnCount,
                finishReason),
            true);
    }

    private static void ReadPlayers(
        JsonElement payload,
        ref string player0Account,
        ref string player1Account,
        ref string player0Leader,
        ref string player1Leader)
    {
        if (!payload.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
            return;

        foreach (var player in players.EnumerateArray())
        {
            var index = ReadInt(player, "index");
            var account = ReadString(player, "accountName") ?? "";
            var leader = FirstDeckLine(ReadString(player, "deckRaw"));
            if (index == 0)
            {
                player0Account = account;
                player0Leader = leader;
            }
            else if (index == 1)
            {
                player1Account = account;
                player1Leader = leader;
            }
        }
    }

    private static int? ReadFirstPlayer(string? kind, JsonElement root, JsonElement payload)
    {
        if (kind is "public_snapshot" or "private_snapshot")
        {
            var fromSnapshot = ReadInt(payload, "firstPlayer");
            return fromSnapshot is 0 or 1 ? fromSnapshot : null;
        }

        if (kind == "player_action_requested"
            && string.Equals(ReadString(payload, "action"), "ChooseFirstPlayer", StringComparison.Ordinal)
            && ReadInt(root, "actor") is int actor and (0 or 1)
            && payload.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("goFirst", out var goFirst)
            && goFirst.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return goFirst.GetBoolean() ? actor : 1 - actor;

        return null;
    }

    private static MatchKind ReadMatchKind(JsonElement payload, MatchKind fallback)
        => ReadString(payload, "matchKind") is { } value
           && Enum.TryParse<MatchKind>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;

    private static string FirstDeckLine(string? deckRaw)
        => deckRaw?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
               .FirstOrDefault()?.Trim().ToUpperInvariant() ?? "";

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private sealed record ParsedMatchLog(LeaderMatchResult? Match, bool Completed);
}
