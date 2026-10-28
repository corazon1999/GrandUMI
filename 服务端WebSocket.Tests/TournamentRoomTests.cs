using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects.Rules;
using GrandUMI.Persistence;
using GrandUMI.Game.Tournaments;
using Xunit;

namespace GrandUMI.Tests;

[Collection("持久化目录隔离")]
public sealed class TournamentRoomTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void 赛事观战在对局中和终局均隐藏手牌且忽略授权标记(bool ended)
    {
        var state = TestScene.MaxScenario(); state.MatchKind = MatchKind.Tournament;
        if (ended) { state.WinnerIndex = 0; state.GameOverReason = "赛事终局"; }
        var view = System.Text.Json.JsonSerializer.SerializeToElement(GrandUMI.Game.Snapshot.StateSnapshotBuilder.Build(state, -1, revealSpectatorMainHand: true));
        Assert.NotEmpty(state.Players[0].Hand);
        Assert.False(view.GetProperty("spectatorHandVisible").GetBoolean());
        Assert.Empty(view.GetProperty("my").GetProperty("handCardNumbers").EnumerateArray());
        Assert.Empty(view.GetProperty("opponent").GetProperty("handCardNumbers").EnumerateArray());
    }

    [Theory]
    [InlineData("tournament_stop")]
    [InlineData("tournament_timeout")]
    public async Task 赛事中立终局日志重启后可恢复且不会生成胜者(string kind)
    {
        TestScene.New();
        var temp = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT")!;
        var root = Path.Combine(temp, "tournament-restore-" + Guid.NewGuid().ToString("N"));
        var previousPersist = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        var previousLogs = Environment.GetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR");
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", Path.Combine(root, "Persist"));
        Environment.SetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR", Path.Combine(root, "MatchLogs"));
        var id = "tr-" + Guid.NewGuid().ToString("N"); var account = "restore-" + id;
        try
        {
            var deck = LegalDeck();
            RoomJournal.Open(id, new { kind = "create", roomId = id, seed = 123456, firstPlayer = 0,
                rulesetId = CardRulesetManager.Current.Id, openingSetupAfterFirstPlayerChoice = false,
                p0 = new { account, displayName = "恢复选手甲", deckRaw = deck },
                p1 = new { account = account + "b", displayName = "恢复选手乙", deckRaw = deck },
                vsBot = false, matchKind = "Tournament", createdAtUtc = DateTime.UtcNow.AddMinutes(-1) });
            RoomJournal.AppendTerminal(id, 1, -1, -1, kind, "赛事等待人工确认", DateTime.UtcNow);
            await GameRoomManager.RestoreAll();
            Assert.True(TerminalOutcomeStore.TryGetByAccount(account, out var terminal));
            Assert.True(terminal.GetProperty("isGameOver").GetBoolean());
            Assert.True(terminal.GetProperty("isDraw").GetBoolean());
            Assert.False(terminal.GetProperty("winnerIsMe").GetBoolean());
            // 终局持久化先提交，房间协调器随后异步删除WAL；全量并发测试也须等待收尾。
            for (var attempt = 0; attempt < 300 && File.Exists(RoomJournal.PathOf(id)); attempt++) await Task.Delay(10);
            Assert.False(File.Exists(RoomJournal.PathOf(id)));
        }
        finally
        {
            GameRoomManager.CleanupRoom(id); RoomJournal.Delete(id);
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", previousPersist);
            Environment.SetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR", previousLogs);
            await Task.Delay(100);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string LegalDeck()
    {
        var leader = CardDatabase.Get("OP15-001")!;
        var cards = CardDatabase.GetBySet("OP15").Where(c => c.Kind != CardKind.Leader && c.SharesColorWith(leader)).SelectMany(c => Enumerable.Repeat(c.Number, 4)).Take(50);
        return string.Join('\n', new[] { leader.Number }.Concat(cards));
    }

    [Fact]
    public async Task 长时间等待人工处理的赛事房间重启后不会被普通空闲期限丢弃()
    {
        TestScene.New();
        var root = Path.Combine(Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT")!, "tournament-long-restore-" + Guid.NewGuid().ToString("N"));
        var previousPersist = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        var previousLogs = Environment.GetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR");
        var roomId = "long-tournament-" + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", Path.Combine(root, "Persist"));
            Environment.SetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR", Path.Combine(root, "MatchLogs"));
            var deck = LegalDeck();
            RoomJournal.Open(roomId, new { kind = "create", roomId, seed = 123456, firstPlayer = 0,
                rulesetId = CardRulesetManager.Current.Id, openingSetupAfterFirstPlayerChoice = false,
                p0 = new { account = roomId + "-a", displayName = "等待处理甲", deckRaw = deck },
                p1 = new { account = roomId + "-b", displayName = "等待处理乙", deckRaw = deck },
                vsBot = false, matchKind = "Tournament", createdAtUtc = DateTime.UtcNow.AddHours(-3) });
            await GameRoomManager.RestoreAll();
            var restored = GameRoomManager.GetRoom(roomId);
            Assert.NotNull(restored); Assert.False(restored.Engine.State.IsGameOver);
            Assert.Equal(MatchKind.Tournament, restored.MatchKind);
        }
        finally
        {
            GameRoomManager.CleanupRoom(roomId); RoomJournal.Delete(roomId); await Task.Delay(100);
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", previousPersist);
            Environment.SetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR", previousLogs);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task 每桌超时互相独立且只提醒管理员并保留进行中的房间()
    {
        await WithRooms(async (first, second) =>
        {
            Assert.Equal(120_000, first.DisconnectGraceRemainingMs[0]);
            Assert.False(first.Engine.State.OperationClockEnabled);
            var previous = TournamentRuntime.Store;
            var (store, tournament) = AttachTournament(first, second);
            try
            {
                TournamentRuntime.Initialize(store);
                await TournamentRuntime.SynchronizeAsync();
                var notified = store.Read(tournament.Id);
                Assert.False(first.Engine.State.IsGameOver); Assert.Null(first.Engine.State.WinnerIndex);
                Assert.False(second.Engine.State.IsGameOver);
                Assert.NotNull(notified.CurrentRound!.Tables[0].TimeoutNotifiedAt);
                Assert.Null(notified.CurrentRound.Tables[1].TimeoutNotifiedAt);
                Assert.False(GameRoomManager.TryCleanupExpiredRoom(first.RoomId, DateTime.UtcNow.AddHours(2)));
                Assert.NotNull(GameRoomManager.GetRoom(first.RoomId));
                await TournamentRuntime.SynchronizeAsync();
                Assert.Equal(notified.Revision, store.Read(tournament.Id).Revision);
                Assert.False(GameRoomManager.IsRankedSettlementEligible(MatchKind.Tournament, first.Engine.State));
            }
            finally { TournamentRuntime.Initialize(previous!); }
        });
    }

    [Fact]
    public async Task 主动结束当前桌进入中立终局并可重复安全执行()
    {
        await WithRooms(async (first, second) =>
        {
            await GameRoomManager.ApplyTournamentDecisionAsync(first.RoomId, "赛事管理员结束本轮，等待逐桌人工结算");
            Assert.True(first.Engine.State.IsDraw); Assert.Contains("等待逐桌人工结算", first.Engine.State.GameOverReason);
            await GameRoomManager.ApplyTournamentDecisionAsync(first.RoomId, "重复结束");
            Assert.Contains("等待逐桌人工结算", first.Engine.State.GameOverReason);
            Assert.False(second.Engine.State.IsGameOver);
        });
    }

    [Fact]
    public async Task 选手退赛终局使用已确认的对手胜利而不是平局()
    {
        await WithRooms(async (first, _) =>
        {
            await GameRoomManager.ApplyTournamentDecisionAsync(first.RoomId, "选手主动退赛", 1);
            Assert.False(first.Engine.State.IsDraw); Assert.Equal(1, first.Engine.State.WinnerIndex);
        });
    }

    [Fact]
    public async Task 赛事终局写盘失败时暂停对局并允许重试结束()
    {
        await WithRooms(async (first, _) =>
        {
            RoomJournal.DurableFailureInjector = (id, operation) => id == first.RoomId && operation == "terminal" ? new IOException("模拟终局落盘失败") : null;
            await Assert.ThrowsAsync<IOException>(() => GameRoomManager.ApplyTournamentDecisionAsync(first.RoomId, "等待人工结算"));
            Assert.True(first.IsRecoveryPaused); Assert.False(first.Engine.State.IsGameOver);
            RoomJournal.DurableFailureInjector = null;
            await GameRoomManager.ApplyTournamentDecisionAsync(first.RoomId, "等待人工结算");
            Assert.True(first.Engine.State.IsDraw);
        });
    }

    [Fact]
    public async Task 淘汰赛超时及旧追加回合均不会自动决胜()
    {
        await WithRooms(async (first, second) =>
        {
            first.Engine.State.TurnCount = 10;
            first.Engine.State.Players[1].LifeArea.Clear();
            var card = first.Engine.State.Players[0].Deck[0];
            first.Engine.State.Players[0].Deck.RemoveAt(0);
            first.Engine.State.Players[0].LifeArea.Add(card);
            var previous = TournamentRuntime.Store;
            var (store, tournament) = AttachTournament(first, second, "cut");
            store.SystemUpdate(tournament.Id, t => { t.CurrentRound!.Tables[0].ExtraTurnLimit = 9; return true; });
            try
            {
                TournamentRuntime.Initialize(store);
                await TournamentRuntime.SynchronizeAsync();
                Assert.False(first.Engine.State.IsGameOver); Assert.Null(first.Engine.State.WinnerIndex);
                Assert.NotNull(store.Read(tournament.Id).CurrentRound!.Tables[0].TimeoutNotifiedAt);
            }
            finally { TournamentRuntime.Initialize(previous!); }
        });
    }

    [Fact]
    public async Task 不限时决赛在很久后仍可继续且人工双败会提交真实中立终局()
    {
        await WithRooms(async (first, second) =>
        {
            var previous = TournamentRuntime.Store;
            var (store, tournament) = AttachTournament(first, second, "cut", final: true);
            try
            {
                TournamentRuntime.Initialize(store);
                await TournamentRuntime.SynchronizeAsync();
                Assert.False(first.Engine.State.IsGameOver);
                Assert.Null(store.Read(tournament.Id).CurrentRound!.Tables[0].TimeoutNotifiedAt);
                await TournamentRuntime.ExecuteAsync(() => store.Execute("manager", "管理者", false, new() {
                    Id = tournament.Id, Action = "adjudicate", RequestId = "final-double-loss", Revision = store.Read(tournament.Id).Revision,
                    TableId = tournament.CurrentRound!.Tables[0].Id, Outcome = "doubleLoss", Reason = "人工判定双方违规",
                }));
                Assert.True(first.Engine.State.IsGameOver); Assert.True(first.Engine.State.IsDraw);
                Assert.Null(first.Engine.State.WinnerIndex); Assert.False(second.Engine.State.IsGameOver);
                Assert.Equal("completed", store.Read(tournament.Id).Status); Assert.Null(store.Read(tournament.Id).Champion);
            }
            finally { TournamentRuntime.Initialize(previous!); }
        });
    }

    private static (TournamentStore Store, Tournament Tournament) AttachTournament(GameRoomManager.RoomEntry first, GameRoomManager.RoomEntry second, string stage = "swiss", bool final = false)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var store = new TournamentStore(Path.Combine(Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR")!, "tournaments.db"));
        var tournament = store.Execute("host", "举办者", false, new() {
            Action = "apply", RequestId = Guid.NewGuid().ToString("N"), Name = "房间计时测试", Capacity = 8,
            RegistrationOpensAt = now, RegistrationClosesAt = now + 600_000, StartsAt = now + 1_800_000, Managers = ["manager"],
        }, now: now);
        store.SystemUpdate(tournament.Id, t => {
            t.Status = stage; t.StartedEntrantCount = final ? 2 : 4;
            var rooms = final ? new[] { first } : new[] { first, second };
            t.Entrants = rooms.SelectMany(room => room.PlayerAccounts.Select(account => new TournamentEntrant { Account = account, Status = "active" })).ToList();
            t.Rounds = [new() { Number = 1, Stage = stage, PublishedAt = now, Tables = rooms.Select((room, index) => new TournamentTable {
                Number = index + 1, Player0 = room.PlayerAccounts[0], Player1 = room.PlayerAccounts[1], RoomId = room.RoomId,
                Status = "playing", StartedAt = index == 0 ? now - 86_400_000 : now,
            }).ToList() }];
            return true;
        });
        return (store, store.Read(tournament.Id));
    }

    private static async Task WithRooms(Func<GameRoomManager.RoomEntry, GameRoomManager.RoomEntry, Task> action)
    {
        TestScene.New();
        var temp = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT") ?? throw new InvalidOperationException("未配置测试临时目录。");
        var root = Path.Combine(temp, "tournament-rooms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var priorPersist = Environment.GetEnvironmentVariable("GRANDUMI_PERSIST_DIR");
        var priorLogs = Environment.GetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR");
        Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", Path.Combine(root, "Persist"));
        Environment.SetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR", Path.Combine(root, "MatchLogs"));
        var rooms = new List<GameRoomManager.RoomEntry>();
        try
        {
            for (var i = 0; i < 2; i++)
            {
                var suffix = Guid.NewGuid().ToString("N"); var deck = LegalDeck();
                rooms.Add(GameRoomManager.CreateRoom("s0-" + suffix, "a0-" + suffix, deck, "s1-" + suffix, "a1-" + suffix, deck, p0First: true, matchKind: MatchKind.Tournament, broadcastInitialState: false));
            }
            await action(rooms[0], rooms[1]);
        }
        finally
        {
            RoomJournal.DurableFailureInjector = null;
            foreach (var room in rooms) GameRoomManager.CleanupRoom(room.RoomId);
            await Task.Delay(100);
            Environment.SetEnvironmentVariable("GRANDUMI_PERSIST_DIR", priorPersist);
            Environment.SetEnvironmentVariable("GRANDUMI_MATCH_LOG_DIR", priorLogs);
            Directory.Delete(root, true);
        }
    }
}
