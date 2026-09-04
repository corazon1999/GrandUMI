using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects.Rules;
using GrandUMI.Game;
using GrandUMI.Game.Logging;
using GrandUMI.Persistence;
using GrandUMI.Training;
using Xunit;

namespace GrandUMIServer.Tests;

[Collection("持久化目录隔离")]
public sealed class InactivityTerminalRecoveryTests
{
    private const string RuntimeId = "grandumi-runtime-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public async Task 挂机终局WAL写盘失败_不得先裁定且房间进入安全暂停()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory("terminal-wal-failure");
        var previousPersist = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        var previousMatchLogs = Environment.GetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR");
        ConfigureDirectories(root);
        GameRoomManager.RoomEntry? room = null;
        try
        {
            room = CreateRoom();
            var remainingBefore = room.Engine.State.InactivityLossRemainingMs;
            RoomJournal.DurableFailureInjector = (roomId, operation) =>
                roomId == room.RoomId && operation == "terminal"
                    ? new IOException("故障演练：终局 WAL 无法落盘")
                    : null;

            var committed = GameRoomManager.FinishByInactivityTimeout(room, expiredPlayer: 0);

            Assert.False(committed);
            Assert.False(room.Engine.State.IsGameOver);
            Assert.Null(room.Engine.State.WinnerIndex);
            Assert.Equal(remainingBefore, room.Engine.State.InactivityLossRemainingMs);
            Assert.True(room.IsRecoveryPaused);
            Assert.Equal("recovery_terminal_commit_failed", room.RecoveryPauseReason);
            var lines = (await RoomJournal.ReadCommittedLinesAsync(RoomJournal.PathOf(room.RoomId))).Lines;
            Assert.Single(lines);
            Assert.Equal("create", Kind(lines[0]));
        }
        finally
        {
            RoomJournal.DurableFailureInjector = null;
            TerminalOutcomeStore.WriteFailureInjector = null;
            if (room is not null) await ForceCleanupAsync(room);
            GameRoomManager.ConfigureCloudReplays(null);
            RestoreDirectories(previousPersist, previousMatchLogs);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task 重复挂机终局与并发清理_只提交一个终局且终局后不再建立断线宽限()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory("terminal-duplicate");
        var previousPersist = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        var previousMatchLogs = Environment.GetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR");
        ConfigureDirectories(root);
        GameRoomManager.RoomEntry? room = null;
        try
        {
            room = CreateRoom();
            var decisions = await Task.WhenAll(
                Task.Run(() => GameRoomManager.FinishByInactivityTimeout(room, 0)),
                Task.Run(() => GameRoomManager.FinishByInactivityTimeout(room, 0)));

            Assert.All(decisions, Assert.True);
            Assert.True(room.Engine.State.IsGameOver);
            Assert.Equal(1, room.Engine.State.WinnerIndex);
            Assert.Equal(0, room.Engine.State.InactivityLossRemainingMs);
            var committed = (await RoomJournal.ReadCommittedLinesAsync(RoomJournal.PathOf(room.RoomId))).Lines;
            Assert.Single(committed.Where(line => Kind(line) == "terminal"));

            // 归零裁定后到清理前收到的迟到断线/重连，不得再修改连接代际、会话或挂宽限任务。
            GameRoomManager.OnPlayerDisconnect(room.PlayerSessionIds[0]);
            Assert.False(room.DisconnectedPlayers[0]);
            var oldSession = room.PlayerSessionIds[0];
            Assert.True(GameRoomManager.TryReclaim(
                $"terminal-reclaim-{Guid.NewGuid():N}", room.PlayerAccounts[0]));
            Assert.Equal(oldSession, room.PlayerSessionIds[0]);

            Parallel.For(0, 8, _ => GameRoomManager.CleanupRoom(room.RoomId));
            await WaitUntilAsync(() => GameRoomManager.GetRoom(room.RoomId) is null);

            Assert.True(TerminalOutcomeStore.TryGetBySession(oldSession, out var terminal));
            Assert.True(terminal.GetProperty("isGameOver").GetBoolean());
            Assert.False(terminal.GetProperty("winnerIsMe").GetBoolean());
            Assert.Equal(
                $"{room.RoomId}:terminal",
                terminal.GetProperty("cinematic").GetProperty("terminal").GetProperty("eventId").GetString());
            Assert.Equal(1, CountMatchLogKind(root, room.RoomId, "match_end"));
        }
        finally
        {
            RoomJournal.DurableFailureInjector = null;
            TerminalOutcomeStore.WriteFailureInjector = null;
            if (room is not null) await ForceCleanupAsync(room);
            GameRoomManager.ConfigureCloudReplays(null);
            RestoreDirectories(previousPersist, previousMatchLogs);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task 终局快照首次写入失败_保留房间与WAL并可并发重试一次收尾()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory("terminal-outcome-retry");
        var previousPersist = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        var previousMatchLogs = Environment.GetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR");
        ConfigureDirectories(root);
        GameRoomManager.RoomEntry? room = null;
        try
        {
            room = CreateRoom();
            Assert.True(GameRoomManager.FinishByInactivityTimeout(room, expiredPlayer: 1));
            TerminalOutcomeStore.WriteFailureInjector = roomId =>
                roomId == room.RoomId ? new IOException("故障演练：终局快照目录不可写") : null;

            GameRoomManager.CleanupRoom(room.RoomId);

            Assert.Same(room, GameRoomManager.GetRoom(room.RoomId));
            Assert.True(File.Exists(RoomJournal.PathOf(room.RoomId)));
            Assert.Contains("终局快照目录不可写", room.TerminalFinalizationFailure, StringComparison.Ordinal);
            Assert.False(File.Exists(TerminalOutcomeStore.PathOf(room.RoomId)));
            var committed = (await RoomJournal.ReadCommittedLinesAsync(RoomJournal.PathOf(room.RoomId))).Lines;
            Assert.Single(committed.Where(line => Kind(line) == "terminal"));

            TerminalOutcomeStore.WriteFailureInjector = null;
            MatchLogRecorder.DurableFailureInjector = (roomId, kind) =>
                roomId == room.RoomId && kind == "match_end"
                    ? new IOException("故障演练：终局审计日志不可写")
                    : null;
            GameRoomManager.CleanupRoom(room.RoomId);
            Assert.Same(room, GameRoomManager.GetRoom(room.RoomId));
            Assert.True(File.Exists(TerminalOutcomeStore.PathOf(room.RoomId)));
            Assert.Contains("终局审计日志不可写", room.TerminalFinalizationFailure, StringComparison.Ordinal);
            Assert.Equal(0, CountMatchLogKind(root, room.RoomId, "match_end"));

            MatchLogRecorder.DurableFailureInjector = null;
            Parallel.For(0, 8, _ => GameRoomManager.CleanupRoom(room.RoomId));
            await WaitUntilAsync(() => GameRoomManager.GetRoom(room.RoomId) is null);

            Assert.True(File.Exists(TerminalOutcomeStore.PathOf(room.RoomId)));
            Assert.Equal(1, CountMatchLogKind(root, room.RoomId, "match_end"));
        }
        finally
        {
            RoomJournal.DurableFailureInjector = null;
            TerminalOutcomeStore.WriteFailureInjector = null;
            MatchLogRecorder.DurableFailureInjector = null;
            if (room is not null) await ForceCleanupAsync(room);
            GameRoomManager.ConfigureCloudReplays(null);
            RestoreDirectories(previousPersist, previousMatchLogs);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task 重启恢复终局WAL_补齐单侧缺失录像并且只收尾一次()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory("terminal-restart");
        var previousPersist = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        var previousMatchLogs = Environment.GetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR");
        ConfigureDirectories(root);
        var roomId = $"terminal-{Guid.NewGuid():N}"[..40];
        var account0 = $"terminal-a-{Guid.NewGuid():N}";
        var account1 = $"terminal-b-{Guid.NewGuid():N}";
        try
        {
            using var cloud = new CloudReplayStore(Path.Combine(root, "CloudReplays"), _ => true);
            cloud.Initialize();
            var capture = cloud.BeginMatch(new CloudReplayMatchStart(
                roomId,
                DateTime.UtcNow.AddMinutes(-1),
                MatchKind.Casual.ToString(),
                Runtime(),
                new CloudReplayPlayer(account0, "终局玩家A", true),
                new CloudReplayPlayer(account1, "终局玩家B", true)))!;
            capture.AppendSnapshot(0, CloudSnapshot(0, false, true, "P0-SECRET", ""));
            capture.AppendSnapshot(1, CloudSnapshot(0, false, false, "P1-SECRET", ""));
            // 模拟进程在同一次终局广播发给 P0 后、发给 P1 前退出。
            capture.AppendSnapshot(0, CloudSnapshot(
                1, true, true, "P0-FINAL", "P1-FINAL", terminalTurnCount: 0));
            GameRoomManager.ConfigureCloudReplays(cloud);

            Directory.CreateDirectory(RoomJournal.GetPersistDir());
            var completedAt = DateTime.UtcNow.AddSeconds(-5);
            await File.WriteAllTextAsync(
                RoomJournal.PathOf(roomId),
                JsonSerializer.Serialize(BuildHeader(roomId, account0, account1)) + "\n"
                + JsonSerializer.Serialize(new
                {
                    kind = "terminal",
                    journalSequence = 1,
                    winnerIndex = 0,
                    expiredPlayer = 1,
                    terminalKind = "inactivity_timeout",
                    reason = "终局玩家B 连续 4 分钟没有操作",
                    completedAtUtc = completedAt,
                    tsUtc = completedAt,
                }) + "\n");

            await GameRoomManager.RestoreAll();
            await WaitUntilAsync(() => GameRoomManager.GetRoom(roomId) is null);
            await WaitUntilAsync(() => !File.Exists(RoomJournal.PathOf(roomId)));
            await GameRoomManager.RestoreAll();

            Assert.True(TerminalOutcomeStore.TryGetByAccount(account0, out var winner));
            Assert.True(winner.GetProperty("isGameOver").GetBoolean());
            Assert.True(winner.GetProperty("winnerIsMe").GetBoolean());
            Assert.Single(cloud.List(account0, CloudQuery()).Items);
            Assert.Single(cloud.List(account1, CloudQuery()).Items);
            Assert.Equal(2,
                cloud.Load(account0, roomId).Document.GetProperty("snapshots").GetArrayLength());
            Assert.Equal(2,
                cloud.Load(account1, roomId).Document.GetProperty("snapshots").GetArrayLength());
            var repairedTerminal = cloud.Load(account1, roomId).Document.GetProperty("snapshots")[1];
            Assert.Equal(
                $"{roomId}:terminal",
                repairedTerminal.GetProperty("cinematic").GetProperty("terminal").GetProperty("eventId").GetString());
            Assert.Equal(1, CountMatchLogKind(root, roomId, "match_end"));
        }
        finally
        {
            GameRoomManager.ConfigureCloudReplays(null);
            GameRoomManager.CleanupRoom(roomId);
            await Task.Delay(50);
            RoomJournal.DurableFailureInjector = null;
            TerminalOutcomeStore.WriteFailureInjector = null;
            RestoreDirectories(previousPersist, previousMatchLogs);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task 发布证明的旧内置规则别名_终局安全收尾且正常房间继续恢复()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory("builtin-ruleset-alias");
        var previousPersist = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        var previousMatchLogs = Environment.GetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR");
        ConfigureDirectories(root);
        var alias = "builtin-dddddddddddddddddddddddddddddddddddddddd";
        var terminalRoomId = $"terminal-{Guid.NewGuid():N}"[..40];
        var normalRoomId = $"normalx-{Guid.NewGuid():N}"[..40];
        var sharedAccount = $"alias-shared-{Guid.NewGuid():N}";
        try
        {
            var manifestPath = Path.Combine(root, CardRulesetManager.BuiltInRecoveryAliasesFileName);
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
            {
                schema = "grandumi.builtin-ruleset-recovery-aliases.v1",
                targetRulesetId = CardRulesetManager.BuiltIn.Id,
                aliases = new[] { alias },
            }));
            CardRulesetManager.InitializeBuiltInRecoveryAliases(manifestPath);
            GameRoomManager.ConfigureCloudReplays(null);
            Directory.CreateDirectory(RoomJournal.GetPersistDir());
            var terminalAt = DateTime.UtcNow.AddSeconds(-5);
            // 先落正常房，再落共用账号的旧终局房，覆盖终局恢复不得破坏较新正常房账号占用。
            await File.WriteAllTextAsync(
                RoomJournal.PathOf(normalRoomId),
                JsonSerializer.Serialize(BuildHeader(
                    normalRoomId,
                    sharedAccount,
                    $"normal-alias-b-{Guid.NewGuid():N}",
                    alias)) + "\n");
            await File.WriteAllTextAsync(
                RoomJournal.PathOf(terminalRoomId),
                JsonSerializer.Serialize(BuildHeader(
                    terminalRoomId,
                    sharedAccount,
                    $"terminal-alias-b-{Guid.NewGuid():N}",
                    alias)) + "\n"
                + JsonSerializer.Serialize(new
                {
                    kind = "action",
                    journalSequence = 1,
                    playerIndex = 0,
                    action = "Surrender",
                    data = new { },
                    requestId = "terminal-alias-surrender",
                    operationSequence = 1,
                    source = "player",
                    tsUtc = terminalAt,
                }) + "\n");
            await GameRoomManager.RestoreAll();
            await WaitUntilAsync(() => GameRoomManager.GetRoom(terminalRoomId) is null);
            await WaitUntilAsync(() => !File.Exists(RoomJournal.PathOf(terminalRoomId)));

            var normal = GameRoomManager.GetRoom(normalRoomId);
            Assert.NotNull(normal);
            Assert.Equal(alias, normal.Engine.State.RulesetId);
            Assert.True(GameRoomManager.HasActivePlayerAccount(sharedAccount));
            Assert.True(TerminalOutcomeStore.ContainsRequired(terminalRoomId));
            var quarantine = Path.Combine(RoomJournal.GetPersistDir(), "quarantine");
            Assert.Empty(Directory.Exists(quarantine)
                ? Directory.GetFiles(quarantine, $"{terminalRoomId}-*")
                    .Concat(Directory.GetFiles(quarantine, $"{normalRoomId}-*"))
                : Array.Empty<string>());
        }
        finally
        {
            GameRoomManager.ConfigureCloudReplays(null);
            GameRoomManager.CleanupRoom(terminalRoomId);
            GameRoomManager.CleanupRoom(normalRoomId);
            await Task.Delay(100);
            RestoreDirectories(previousPersist, previousMatchLogs);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task 普通终局已有权威快照_超过无操作TTL仍须重放并完成收尾()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory("terminal-outcome-over-ttl");
        var previousPersist = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        var previousMatchLogs = Environment.GetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR");
        ConfigureDirectories(root);
        var roomId = $"terminal-{Guid.NewGuid():N}"[..40];
        var account0 = $"terminal-a-{Guid.NewGuid():N}";
        var account1 = $"terminal-b-{Guid.NewGuid():N}";
        try
        {
            GameRoomManager.ConfigureCloudReplays(null);
            Directory.CreateDirectory(RoomJournal.GetPersistDir());
            var completedAt = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(45));
            await File.WriteAllTextAsync(
                RoomJournal.PathOf(roomId),
                JsonSerializer.Serialize(BuildHeader(roomId, account0, account1)) + "\n"
                + JsonSerializer.Serialize(new
                {
                    kind = "action",
                    journalSequence = 1,
                    playerIndex = 0,
                    action = "Surrender",
                    data = new { },
                    requestId = "terminal-over-ttl-surrender",
                    operationSequence = 1,
                    source = "player",
                    tsUtc = completedAt,
                }) + "\n");
            var placeholder = JsonSerializer.SerializeToElement(new { proto = "MsgGameState" });
            var record = new TerminalOutcomeRecord(
                TerminalOutcomeStore.SchemaVersion,
                roomId,
                completedAt,
                MatchKind.Casual.ToString(),
                WinnerIndex: 1,
                IsDraw: false,
                Reason: "终局玩家A 投降",
                Accounts: [account0, account1],
                SessionIds: ["offline-0", "offline-1"],
                PlayerSnapshots: [placeholder, placeholder]);
            Directory.CreateDirectory(TerminalOutcomeStore.GetDirectory());
            await File.WriteAllTextAsync(
                TerminalOutcomeStore.PathOf(roomId),
                JsonSerializer.Serialize(record, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                }));

            await GameRoomManager.RestoreAll();
            await WaitUntilAsync(() => GameRoomManager.GetRoom(roomId) is null);
            await WaitUntilAsync(() => !File.Exists(RoomJournal.PathOf(roomId)));

            Assert.True(File.Exists(TerminalOutcomeStore.PathOf(roomId)));
            Assert.Equal(1, CountMatchLogKind(root, roomId, "match_end"));
        }
        finally
        {
            GameRoomManager.ConfigureCloudReplays(null);
            GameRoomManager.CleanupRoom(roomId);
            await Task.Delay(50);
            RestoreDirectories(previousPersist, previousMatchLogs);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task 普通终局快照已过期_云回放终局帧仍须阻止TTL弃局并幂等收尾()
    {
        GrandUMI.Tests.TestScene.New();
        var root = TestDirectory("terminal-cloud-evidence-over-ttl");
        var previousPersist = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        var previousMatchLogs = Environment.GetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR");
        ConfigureDirectories(root);
        var roomId = $"terminal-{Guid.NewGuid():N}"[..40];
        var account0 = $"terminal-a-{Guid.NewGuid():N}";
        var account1 = $"terminal-b-{Guid.NewGuid():N}";
        try
        {
            using var cloud = new CloudReplayStore(Path.Combine(root, "CloudReplays"), _ => true);
            cloud.Initialize();
            var capture = cloud.BeginMatch(new CloudReplayMatchStart(
                roomId,
                DateTime.UtcNow.AddMinutes(-46),
                MatchKind.Casual.ToString(),
                Runtime(),
                new CloudReplayPlayer(account0, "终局玩家A", true),
                new CloudReplayPlayer(account1, "终局玩家B", true)))!;
            capture.AppendSnapshot(0, CloudSnapshot(1, false, true, "P0-SECRET", ""));
            capture.AppendSnapshot(1, CloudSnapshot(1, false, false, "P1-SECRET", ""));
            capture.AppendSnapshot(0, CloudSnapshot(
                2, true, false, "P0-FINAL", "P1-FINAL", "终局玩家A 投降"));
            capture.AppendSnapshot(1, CloudSnapshot(
                2, true, true, "P1-FINAL", "P0-FINAL", "终局玩家A 投降"));
            GameRoomManager.ConfigureCloudReplays(cloud);

            Directory.CreateDirectory(RoomJournal.GetPersistDir());
            var completedAt = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(45));
            await File.WriteAllTextAsync(
                RoomJournal.PathOf(roomId),
                JsonSerializer.Serialize(BuildHeader(roomId, account0, account1)) + "\n"
                + JsonSerializer.Serialize(new
                {
                    kind = "action",
                    journalSequence = 1,
                    playerIndex = 0,
                    action = "Surrender",
                    data = new { },
                    requestId = "terminal-cloud-over-ttl-surrender",
                    operationSequence = 1,
                    source = "player",
                    tsUtc = completedAt,
                }) + "\n");

            Assert.False(File.Exists(TerminalOutcomeStore.PathOf(roomId)));
            await GameRoomManager.RestoreAll();
            await WaitUntilAsync(() => GameRoomManager.GetRoom(roomId) is null);
            await WaitUntilAsync(() => !File.Exists(RoomJournal.PathOf(roomId)));
            await GameRoomManager.RestoreAll();

            Assert.Empty(Directory.Exists(Path.Combine(RoomJournal.GetPersistDir(), "quarantine"))
                ? Directory.GetFiles(Path.Combine(RoomJournal.GetPersistDir(), "quarantine"), $"{roomId}-*.jsonl")
                : Array.Empty<string>());
            Assert.Single(cloud.List(account0, CloudQuery()).Items);
            Assert.Single(cloud.List(account1, CloudQuery()).Items);
            var loserReplay = cloud.Load(account0, roomId).Document;
            Assert.True(loserReplay.GetProperty("snapshots")[1].GetProperty("isGameOver").GetBoolean());
            Assert.False(loserReplay.GetProperty("snapshots")[1].GetProperty("winnerIsMe").GetBoolean());
            Assert.Equal(1, CountMatchLogKind(root, roomId, "match_end"));
        }
        finally
        {
            GameRoomManager.ConfigureCloudReplays(null);
            GameRoomManager.CleanupRoom(roomId);
            await Task.Delay(50);
            RestoreDirectories(previousPersist, previousMatchLogs);
            TryDelete(root);
        }
    }

    private static GameRoomManager.RoomEntry CreateRoom()
    {
        GameRoomManager.ConfigureCloudReplays(null);
        var suffix = Guid.NewGuid().ToString("N");
        var deck = BuildLegalDeck("OP15-001");
        return GameRoomManager.CreateRoom(
            $"terminal-s0-{suffix}", $"terminal-a-{suffix}", deck,
            $"terminal-s1-{suffix}", $"terminal-b-{suffix}", deck,
            p0First: true,
            matchKind: MatchKind.Casual,
            broadcastInitialState: false);
    }

    private static object BuildHeader(
        string roomId,
        string account0,
        string account1,
        string rulesetId = "builtin-test")
    {
        var deck = BuildLegalDeck("OP15-001");
        return new
        {
            kind = "create",
            roomId,
            seed = 123456,
            firstPlayer = 0,
            rulesetId,
            openingSetupAfterFirstPlayerChoice = false,
            p0 = new { account = account0, displayName = "终局玩家A", deckRaw = deck },
            p1 = new { account = account1, displayName = "终局玩家B", deckRaw = deck },
            vsBot = false,
            matchKind = MatchKind.Casual.ToString(),
            createdAtUtc = DateTime.UtcNow.AddMinutes(-1),
        };
    }

    private static string BuildLegalDeck(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet("OP15")
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leaderNumber };
        var counts = new Dictionary<string, int>();
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            lines.Add(card.Number);
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
        }
        return string.Join('\n', lines);
    }

    private static object CloudSnapshot(
        int tick,
        bool isGameOver,
        bool winnerIsMe,
        string myHand,
        string opponentHand,
        string? terminalReason = null,
        int terminalTurnCount = 1)
        => new
        {
            proto = "MsgGameState",
            tick,
            viewerKind = "player",
            phase = "Main",
            currentTurn = true,
            turnCount = terminalTurnCount,
            isGameOver,
            winnerIsMe,
            isDraw = false,
            gameOverReason = isGameOver
                ? terminalReason ?? "终局玩家B 连续 4 分钟没有操作"
                : null,
            diceWinnerIsMe = true,
            isFirstPlayer = true,
            requestId = (string?)null,
            actionPayload = "",
            pendingPrompt = (object?)null,
            replayHands = (object?)null,
            my = CloudPlayer("我方", "OP15-001", myHand),
            opponent = CloudPlayer("对手", "OP15-001", opponentHand),
        };

    private static object CloudPlayer(string name, string leader, string hand)
        => new
        {
            name,
            leaderNumber = leader,
            handCardIds = string.IsNullOrEmpty(hand) ? Array.Empty<string>() : new[] { "id-" + hand },
            handCardNumbers = string.IsNullOrEmpty(hand) ? Array.Empty<string>() : new[] { hand },
            handCardCosts = string.IsNullOrEmpty(hand) ? Array.Empty<int>() : new[] { 1 },
            handCardCounters = string.IsNullOrEmpty(hand) ? Array.Empty<int>() : new[] { 1000 },
            trashNumbers = Array.Empty<string>(),
            fieldCards = Array.Empty<object>(),
        };

    private static ReplayRuntimeIdentity Runtime()
        => new(
            "grandumi.matchlog.v1",
            "grandumi.matchlog-adapter.v1",
            RuntimeId,
            new string('1', 40),
            "sha256:" + new string('2', 64),
            "builtin-v1",
            "sha256:" + new string('3', 64),
            "sha256:" + new string('4', 64),
            "dotnet-random-test",
            "guid-test",
            "opening-test",
            "grandumi.replay-config.v1",
            "sha256:" + new string('5', 64));

    private static CloudReplayListQuery CloudQuery()
        => new(null, null, null, false, null, null, 0, 20);

    private static string Kind(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.GetProperty("kind").GetString() ?? "";
    }

    private static int CountMatchLogKind(string root, string roomId, string kind)
    {
        var logs = Path.Combine(root, "MatchLogs");
        return Directory.Exists(logs)
            ? Directory.GetFiles(logs, $"{roomId}.jsonl", SearchOption.AllDirectories)
                .SelectMany(ReadSharedLines)
                .Count(line => Kind(line) == kind)
            : 0;
    }

    private static IEnumerable<string> ReadSharedLines(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) yield return line;
    }

    private static void ConfigureDirectories(string root)
    {
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", Path.Combine(root, "Persist"));
        Environment.SetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR", Path.Combine(root, "MatchLogs"));
    }

    private static void RestoreDirectories(string? persist, string? matchLogs)
    {
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", persist);
        Environment.SetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR", matchLogs);
    }

    private static async Task ForceCleanupAsync(GameRoomManager.RoomEntry room)
    {
        GameRoomManager.CleanupRoom(room.RoomId);
        for (var attempt = 0; attempt < 100 && GameRoomManager.GetRoom(room.RoomId) is not null; attempt++)
            await Task.Delay(10);
        if (GameRoomManager.GetRoom(room.RoomId) is not null)
        {
            room.Engine.State.WinnerIndex = null;
            room.Engine.State.IsDraw = false;
            room.Engine.State.GameOverReason = null;
            GameRoomManager.CleanupRoom(room.RoomId);
        }
        await Task.Delay(50);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 300 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition(), "终局收尾没有在预期时间内完成");
    }

    private static string TestDirectory(string name)
    {
        var root = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT")
            ?? throw new InvalidOperationException("终局恢复测试必须设置 GRANDUMI_TEST_TEMP_ROOT。");
        return Path.Combine(root, "terminal-recovery", $"{name}-{Guid.NewGuid():N}");
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
