using GrandUMI.Game.Tournaments;
using Xunit;

namespace GrandUMI.Tests;

public sealed partial class TournamentStoreTests
{
    private Tournament Underfilled(int actual, int capacity = 8, string prefix = "u")
    {
        var t = Create(capacity);
        for (var i = 0; i < actual; i++)
        {
            Command(t.Id, "register", prefix + i, deck: Deck("测试卡组"));
            Command(t.Id, "checkin", prefix + i);
        }
        return Command(t.Id, "openRound", "host");
    }

    [Theory]
    [InlineData(2, 8, 1, 2)] [InlineData(3, 8, 2, 2)] [InlineData(5, 8, 3, 4)]
    [InlineData(7, 8, 3, 4)] [InlineData(8, 8, 3, 4)] [InlineData(10, 16, 4, 4)] [InlineData(12, 16, 4, 4)] [InlineData(17, 32, 5, 8)]
    public void 未满员可由举办者提前开赛并完成实际人数赛程(int actual, int capacity, int rounds, int cut)
    {
        var t = Underfilled(actual, capacity);
        Assert.Equal(actual, t.StartedEntrantCount); Assert.Equal(Now, t.ActualStartedAt);
        Assert.Equal(rounds, t.SwissRounds); Assert.Equal(cut, t.CutSize); Assert.Equal(capacity, t.Capacity);
        var pairs = new HashSet<string>(); var byes = new List<string>();
        for (var r = 0; r < rounds; r++)
        {
            var state = _store.Read(t.Id);
            Assert.Equal(rounds, state.SwissRounds);
            Assert.Equal(actual % 2, state.CurrentRound!.Tables.Count(m => m.Outcome == "bye"));
            foreach (var table in state.CurrentRound.Tables)
                if (table.Player1 is null) byes.Add(table.Player0!);
                else Assert.True(pairs.Add(string.Join(':', new[] { table.Player0!, table.Player1 }.Order())));
            NaturalFinish(t.Id);
            if (r + 1 < rounds) Command(t.Id, "openRound");
        }
        Assert.Equal(byes.Count, byes.Distinct().Count());
        var playoff = Command(t.Id, "openRound");
        Assert.Equal("cut", playoff.CurrentRound!.Stage); Assert.Equal(cut / 2, playoff.CurrentRound.Tables.Count);
        for (var i = 0; i < 8 && _store.Read(t.Id).Status != "completed"; i++)
        {
            NaturalFinish(t.Id);
            if (_store.Read(t.Id).Status != "completed") Command(t.Id, "openRound");
        }
        Assert.Equal("completed", _store.Read(t.Id).Status); Assert.NotNull(_store.Read(t.Id).Champion);
    }

    [Fact]
    public void 少于两人拒绝开赛且不能越权或用旧版本锁名单()
    {
        var t = Create(); Command(t.Id, "register", "u0", deck: Deck("卡组")); Command(t.Id, "checkin", "u0");
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "openRound", "host"));
        Command(t.Id, "register", "u1", deck: Deck("卡组"));
        var before = _store.Read(t.Id); Command(t.Id, "checkin", "u1");
        Assert.Throws<InvalidOperationException>(() => _store.Execute("host", "举办者", false, new() { Action = "openRound", Id = t.Id, RequestId = "stale-start", Revision = before.Revision, ConfirmStart = true }, now: Now));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "openRound", "u0"));
        var started = Command(t.Id, "openRound", "host");
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "register", "u2", deck: Deck("卡组")));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "checkin", "u0"));
        Assert.Equal(2, started.StartedEntrantCount);
    }

    [Theory]
    [InlineData(-600_000)] [InlineData(600_000)]
    public void 举办者可提前或推迟计划时间且保留审核卡组及签到(long offset)
    {
        var t = Create(); Command(t.Id, "register", "u0", deck: Deck("卡组")); Command(t.Id, "checkin", "u0");
        var changed = Command(t.Id, "reschedule", "HOST", r => { r.StartsAt = Start + offset; r.RegistrationClosesAt = Start + offset - 300_000; r.Reason = "与参赛者协调时间"; });
        Assert.Equal("approved", changed.Status); Assert.Equal(Start + offset, changed.StartsAt); Assert.True(changed.Entrants[0].CheckedIn);
        Assert.Equal("pinned-rules", changed.RulesetId); Assert.Equal("卡组", changed.Entrants[0].Deck.Name);
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "reschedule", "u0", r => { r.StartsAt = Start; r.RegistrationClosesAt = Start - 300_000; r.Reason = "越权"; }));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "reschedule", "host", r => { r.StartsAt = Start; r.RegistrationClosesAt = Start - 300_000; }));
    }

    [Fact]
    public void 移出须填写原因并禁止重新报名且已签到候补递补()
    {
        var t = Create();
        for (var i = 0; i < 9; i++) { Command(t.Id, "register", "u" + i, deck: Deck("卡组")); Command(t.Id, "checkin", "u" + i); }
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "removeEntrant", "u1", r => { r.TargetAccount = "u0"; r.Reason = "越权"; }));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "removeEntrant", "host", r => r.TargetAccount = "u0"));
        var changed = Command(t.Id, "removeEntrant", "host", r => { r.TargetAccount = "U0"; r.Reason = "违反赛事约定"; });
        Assert.Equal("disqualified", changed.Entrants[0].Status); Assert.Equal("违反赛事约定", changed.Entrants[0].LeaveReason);
        Assert.Equal("registered", changed.Entrants[8].Status); Assert.Equal(100, _store.ReadReputation("u0"));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "register", "u0", deck: Deck("卡组")));
        var view = GrandUMI.WebSocketBridge.BuildTournamentResponse(changed, "u0", false, "reason").GetProperty("tournament");
        Assert.Equal("违反赛事约定", view.GetProperty("mine").GetProperty("leaveReason").GetString());
        Assert.True(GrandUMI.WebSocketBridge.BuildTournamentResponse(changed, "host", false, "host").GetProperty("tournament").GetProperty("canHost").GetBoolean());
    }

    [Fact]
    public void 已签到退赛二次确认后原子扣分并对重试和旧版本幂等()
    {
        var t = Underfilled(5); var table = t.CurrentRound!.Tables.First(m => m.Player1 is not null);
        var player = table.Player0!;
        var before = _store.Read(t.Id);
        var request = new TournamentCommand { Id = t.Id, Action = "withdraw", Revision = before.Revision, RequestId = "withdraw-once", ConfirmWithdrawal = true };
        var left = _store.Execute(player, player, false, request, now: Now);
        Assert.Equal("withdrawn", left.Entrants.First(e => e.Account == player).Status);
        Assert.Equal(table.Player1, left.CurrentRound!.Tables.First(m => m.Id == table.Id).Winner);
        Assert.Equal(85, _store.ReadReputation(player));
        _store.Execute(player.ToUpperInvariant(), player, false, request, now: Now);
        Assert.Equal(85, _store.ReadReputation(player)); Assert.Single(_store.ReadReputationHistory(player));
        request.RequestId = "withdraw-stale";
        Assert.Throws<InvalidOperationException>(() => _store.Execute(player, player, false, request, now: Now));
        var reloaded = new TournamentStore(Path.Combine(_root, "tournaments.db"));
        Assert.Equal(85, reloaded.ReadReputation(player.ToUpperInvariant()));
        TournamentRuntime.RecordResult(_store, table.RoomId ?? "missing", 0, "迟到胜利", DateTime.UtcNow);
        NaturalFinish(t.Id); var next = Command(t.Id, "openRound");
        Assert.Equal(3, next.SwissRounds); Assert.DoesNotContain(next.CurrentRound!.Tables, m => m.Player0 == player || m.Player1 == player);
        Assert.DoesNotContain(next.CurrentRound.Tables, m => m.Outcome == "bye");
    }

    [Fact]
    public void 手动结算时退赛与移出不跳过逐桌确认或申诉门禁()
    {
        var t = Ready(); var table = t.CurrentRound!.Tables[0]; ForceEnd(t.Id);
        Command(t.Id, "withdraw", table.Player0!, r => r.ConfirmWithdrawal = true);
        Command(t.Id, "removeEntrant", "host", r => { r.TargetAccount = table.Player1!; r.Reason = "无法继续参赛"; });
        var manual = _store.Read(t.Id); var match = manual.CurrentRound!.Tables[0];
        Assert.Equal("manual", manual.CurrentRound.Status); Assert.False(match.Confirmed); Assert.Equal("doubleLoss", match.Outcome);
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "openRound"));
        foreach (var m in manual.CurrentRound.Tables) Command(t.Id, "settle", configure: r => { r.TableId = m.Id; r.Outcome = m.Id == table.Id ? "doubleLoss" : "p0"; r.Reason = "逐桌核实"; });
        Assert.Equal("settled", _store.Read(t.Id).CurrentRound!.Status);
    }

    [Fact]
    public void 退赛扣分最低为零且未签到退出不扣分()
    {
        var t = Create();
        for (var i = 0; i < 8; i++)
        {
            Command(t.Id, "register", "u0", deck: Deck("卡组")); Command(t.Id, "checkin", "u0");
            Command(t.Id, "withdraw", "u0", r => r.ConfirmWithdrawal = true);
        }
        Assert.Equal(0, _store.ReadReputation("u0"));
        Command(t.Id, "register", "u1", deck: Deck("卡组")); Command(t.Id, "withdraw", "u1");
        Assert.Equal(100, _store.ReadReputation("u1")); Assert.Empty(_store.ReadReputationHistory("u1"));
    }

    [Fact]
    public void 正常完赛加五分且封顶一百并且重启或重复保存不重复奖励()
    {
        var penalty = Create(); Command(penalty.Id, "register", "u0", deck: Deck("卡组")); Command(penalty.Id, "checkin", "u0");
        Command(penalty.Id, "withdraw", "u0", r => r.ConfirmWithdrawal = true);
        var t = Underfilled(5);
        for (var i = 0; i < 10 && _store.Read(t.Id).Status != "completed"; i++)
        {
            NaturalFinish(t.Id);
            if (_store.Read(t.Id).Status != "completed") Command(t.Id, "openRound");
        }
        Assert.Equal("completed", _store.Read(t.Id).Status);
        Assert.Equal(90, _store.ReadReputation("u0")); Assert.Equal(100, _store.ReadReputation("u1"));
        _store.SystemUpdate(t.Id, state => true);
        Assert.Equal(90, new TournamentStore(Path.Combine(_root, "tournaments.db")).ReadReputation("u0"));
        Assert.Single(_store.ReadReputationHistory("u0").Where(e => e.Reason == "正常完成赛事"));
    }

    [Fact]
    public void 代办退赛需确认且扣分而强制移出不扣分()
    {
        var t = Underfilled(5);
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "removeEntrant", "host", r => { r.TargetAccount = "u0"; r.WithdrawEntrant = true; r.Reason = "选手申请退赛"; }));
        Command(t.Id, "removeEntrant", "host", r => { r.TargetAccount = "u0"; r.WithdrawEntrant = true; r.ConfirmWithdrawal = true; r.Reason = "选手申请退赛"; });
        Assert.Equal(85, _store.ReadReputation("u0"));
        Command(t.Id, "removeEntrant", "host", r => { r.TargetAccount = "u1"; r.Reason = "违反赛事规则"; });
        Assert.Equal(100, _store.ReadReputation("u1"));
    }

    [Fact]
    public void 人工确认后的退赛桌不能被另一名选手退赛改写()
    {
        var t = Ready(); var table = t.CurrentRound!.Tables[0]; ForceEnd(t.Id);
        Command(t.Id, "withdraw", table.Player0!, r => r.ConfirmWithdrawal = true);
        Command(t.Id, "settle", configure: r => { r.TableId = table.Id; r.Outcome = "p1"; r.Reason = "独立确认乙方胜"; });
        Command(t.Id, "withdraw", table.Player1!, r => r.ConfirmWithdrawal = true);
        var result = _store.Read(t.Id).CurrentRound!.Tables[0];
        Assert.True(result.Confirmed); Assert.Equal("p1", result.Outcome); Assert.Equal("独立确认乙方胜", result.Reason);
    }

    [Fact]
    public void 中途移出保留积分且偶数变奇数的下一轮安排轮空()
    {
        var t = Ready(); NaturalFinish(t.Id);
        var oldPoints = TournamentStore.Standings(_store.Read(t.Id)).First(s => s.Account == "p0").Points;
        Command(t.Id, "removeEntrant", "host", r => { r.TargetAccount = "p0"; r.Reason = "无法继续参赛"; });
        var next = Command(t.Id, "openRound");
        Assert.Single(next.CurrentRound!.Tables.Where(m => m.Outcome == "bye"));
        Assert.Equal(oldPoints, TournamentStore.Standings(next).First(s => s.Account == "p0").Points);
        Assert.DoesNotContain(next.CurrentRound.Tables, m => m.Player0 == "p0" || m.Player1 == "p0");
    }

    [Fact]
    public void 淘汰赛仍可退赛并判对手胜但不会影响已完成战绩()
    {
        var t = Ready();
        for (var i = 0; i < 3; i++) { NaturalFinish(t.Id); if (i < 2) Command(t.Id, "openRound"); }
        var cut = Command(t.Id, "openRound"); var table = cut.CurrentRound!.Tables[0];
        var left = Command(t.Id, "withdraw", table.Player0!, r => r.ConfirmWithdrawal = true);
        Assert.Equal(table.Player1, left.CurrentRound!.Tables[0].Winner); Assert.Equal(85, _store.ReadReputation(table.Player0!));
        Assert.Equal("withdrawn", left.Entrants.First(e => e.Account == table.Player0).Status);
        Assert.Equal(3, left.Rounds.Count(r => r.Stage == "swiss"));
    }

    [Fact]
    public void 只剩一人须等全桌结算及申诉关闭后才能推进夺冠()
    {
        var t = Underfilled(3); var keeper = t.CurrentRound!.Tables.First(m => m.Player1 is null).Player0!;
        foreach (var member in t.Entrants.Where(e => e.Account != keeper)) Command(t.Id, "withdraw", member.Account, r => r.ConfirmWithdrawal = true);
        Assert.Equal("settled", _store.Read(t.Id).CurrentRound!.Status);
        var completed = Command(t.Id, "openRound"); Assert.Equal("completed", completed.Status); Assert.Equal(keeper, completed.Champion);
        Assert.Empty(_store.ReadReputationHistory(keeper));
    }

    [Fact]
    public void 参赛举办者不能移出自己未结束比赛的对手()
    {
        var t = Underfilled(4); var match = t.CurrentRound!.Tables[0];
        _store.SystemUpdate(t.Id, state => { state.Owner = match.Player0!; return true; });
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "removeEntrant", match.Player0!, r => { r.TargetAccount = match.Player1!; r.Reason = "不能自己判胜"; }));
        Command(t.Id, "removeEntrant", "manager", r => { r.TargetAccount = match.Player1!; r.Reason = "独立管理员核实后处理"; });
        Assert.Equal(match.Player0, _store.Read(t.Id).CurrentRound!.Tables[0].Winner);
    }
}
