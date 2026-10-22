using GrandUMI.Game.Tournaments;
using GrandUMI.Persistence;
using Xunit;

namespace GrandUMI.Tests;

public sealed class TournamentStoreTests : IDisposable
{
    private const long Now = 1_800_000_000_000;
    private const long Start = Now + 1_800_000;
    private readonly string _root;
    private readonly TournamentStore _store;
    public TournamentStoreTests()
    {
        var temp = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT") ?? throw new InvalidOperationException("请配置项目测试临时目录。");
        _root = Path.Combine(temp, "tournament-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root); _store = new(Path.Combine(_root, "tournaments.db"));
    }
    public void Dispose() { Directory.Delete(_root, true); }
    private TournamentCommand Application(int count = 8, long start = Start) => new()
    {
        Action = "apply", RequestId = Guid.NewGuid().ToString("N"), Name = "社区测试赛", Description = "独立落座回归", Capacity = count,
        RegistrationOpensAt = Now, RegistrationClosesAt = start - 300_000, StartsAt = start, Managers = ["manager"], Judges = ["judge"],
    };
    private Tournament Create(int count = 8, long start = Start)
    {
        var t = _store.Execute("host", "举办者", false, Application(count, start), now: Now);
        return Command(t.Id, "approve", "admin", admin: true);
    }
    private Tournament Command(string id, string action, string actor = "manager", Action<TournamentCommand>? configure = null, long at = Now, bool admin = false, StoredDeck? deck = null)
    {
        var r = new TournamentCommand { Id = id, Action = action, Revision = _store.Read(id).Revision, RequestId = Guid.NewGuid().ToString("N"), ConfirmCheckin = action == "checkin" };
        configure?.Invoke(r); return _store.Execute(actor, actor, admin, r, deck, at, "pinned-rules", "hash");
    }
    private Tournament Ready(int count = 8)
    {
        var t = Create(count);
        for (var i = 0; i < count; i++) Command(t.Id, "register", "p" + i, deck: Deck("卡组" + i));
        for (var i = 0; i < count; i++) Command(t.Id, "checkin", "p" + i, at: Start - 600_000);
        return Command(t.Id, "openRound", at: Start);
    }
    private static StoredDeck Deck(string name) => new() { Name = name, Leader = "OP01-001", Cards = Enumerable.Repeat("OP01-006", 50).ToArray() };
    private void NaturalFinish(string id)
    {
        _store.SystemUpdate(id, t => { foreach (var m in t.CurrentRound!.Tables.Where(m => !m.Confirmed)) TournamentStore.Finish(m, "p0", "对局正常结束", "system", Start); TournamentStore.RefreshRound(t); return true; });
    }
    private Tournament ForceEnd(string id)
    {
        var t = _store.Read(id);
        return Command(id, "endRound", configure: r => { r.ConfirmationToken = TournamentStore.EndToken(t, "manager"); r.Reason = "裁判决定结束本轮"; });
    }

    [Theory]
    [InlineData(8, true)] [InlineData(16, true)] [InlineData(32, true)] [InlineData(64, true)] [InlineData(128, true)] [InlineData(256, true)]
    [InlineData(2, false)] [InlineData(4, false)] [InlineData(10, false)] [InlineData(12, false)] [InlineData(24, false)] [InlineData(512, false)]
    public void 人数仅接受已批准的二次幂规格(int value, bool valid) => Assert.Equal(valid, TournamentStore.IsCapacityValid(value));

    [Fact]
    public void 断线宽限单独持久化且只递减不干扰轮次确认版本()
    {
        var t = Ready();
        _store.SaveDisconnectGrace("room-grace", 119_000, 90_000);
        _store.SaveDisconnectGrace("room-grace", 120_000, 80_000);
        var reloaded = new TournamentStore(Path.Combine(_root, "tournaments.db"));
        Assert.Equal(new long[] { 119_000, 80_000 }, reloaded.ReadDisconnectGrace("room-grace"));
        Assert.Equal(t.Revision, reloaded.Read(t.Id).Revision);
    }

    [Fact]
    public void 裁判可为恢复受阻的淘汰赛明确胜者但不能双败或在完赛后改写()
    {
        var t = Ready();
        for (var i = 0; i < 3; i++) { NaturalFinish(t.Id); if (i < 2) Command(t.Id, "openRound", at: Start); }
        var cut = Command(t.Id, "openRound", at: Start); var table = cut.CurrentRound!.Tables[0];
        _store.SystemUpdate(t.Id, state => { state.CurrentRound!.Tables[0].Status = "blocked"; return true; });
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "correct", "judge", r => { r.TableId = table.Id; r.Outcome = "doubleLoss"; r.Reason = "不能双败"; }));
        var corrected = Command(t.Id, "correct", "judge", r => { r.TableId = table.Id; r.Outcome = "p1"; r.Reason = "核对恢复日志后裁定"; });
        Assert.Equal(table.Player1, corrected.CurrentRound!.Tables[0].Winner);
        NaturalFinish(t.Id); Command(t.Id, "openRound", at: Start); NaturalFinish(t.Id);
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "correct", "judge", r => { r.TableId = _store.Read(t.Id).CurrentRound!.Tables[0].Id; r.Outcome = "p1"; r.Reason = "已完赛不能改写"; }));
    }

    [Fact]
    public void 受阻桌过落座窗口后仍能重新落座且中断连续缺席计数()
    {
        var t = Ready(); var table = t.CurrentRound!.Tables[0];
        _store.SystemUpdate(t.Id, state => { state.CurrentRound!.Tables[0].Status = "blocked"; state.Entrants.First(e => e.Account == table.Player0).MissedRounds = 1; return true; });
        var first = Command(t.Id, "seat", table.Player0!, r => r.TableId = table.Id, Start + 600_000);
        Assert.Equal("blocked", first.CurrentRound!.Tables[0].Status);
        Assert.Equal(0, first.Entrants.First(e => e.Account == table.Player0).MissedRounds);
        var second = Command(t.Id, "seat", table.Player1!, r => r.TableId = table.Id, Start + 601_000);
        Assert.Equal("starting", second.CurrentRound!.Tables[0].Status);
    }

    [Fact]
    public void 二百五十六人赛事完整八轮不重复交手且晋级十六强()
    {
        var t = Ready(256); var pairs = new HashSet<string>();
        for (var round = 0; round < 8; round++)
        {
            var current = _store.Read(t.Id).CurrentRound!;
            Assert.Equal(128, current.Tables.Count);
            foreach (var table in current.Tables) Assert.True(pairs.Add(string.Join(':', new[] { table.Player0!, table.Player1! }.Order())));
            NaturalFinish(t.Id);
            if (round != 7) Command(t.Id, "openRound", at: Start);
        }
        var cut = Command(t.Id, "openRound", at: Start);
        Assert.Equal("cut", cut.CurrentRound!.Stage); Assert.Equal(8, cut.CurrentRound.Tables.Count);
        Assert.Equal(16, cut.CurrentRound.Tables.SelectMany(m => new[] { m.Player0, m.Player1 }).Distinct().Count());
    }

    [Fact]
    public void 申请未经审核不对其他玩家公开且不能报名()
    {
        var t = _store.Execute("host", "举办者", false, Application(), now: Now);
        Assert.False(TournamentStore.CanView(t, "other", false));
        Assert.True(TournamentStore.CanView(t, "host", false));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "register", "other", deck: Deck("卡组")));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "approve", "host"));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "reject", "admin", admin: true));
    }

    [Theory]
    [InlineData("other", false, false)]
    [InlineData("p0", false, false)]
    [InlineData("P0", false, false)]
    [InlineData("host", false, true)]
    [InlineData("HOST", false, true)]
    [InlineData("manager", false, true)]
    [InlineData("MANAGER", false, true)]
    [InlineData("judge", false, true)]
    [InlineData("JUDGE", false, true)]
    [InlineData("admin", true, true)]
    public void 已取消赛事仅举办者及工作人员可查看且列表与详情使用相同权限(string actor, bool admin, bool visible)
    {
        var t = Create();
        Command(t.Id, "register", "p0", deck: Deck("参赛卡组"));
        Command(t.Id, "cancel", configure: r => r.Reason = "取消测试赛");
        var reloaded = new TournamentStore(Path.Combine(_root, "tournaments.db"));

        Assert.Equal(visible, TournamentStore.CanView(reloaded.Read(t.Id), actor, admin));
        Assert.Equal(visible, reloaded.ReadAll().Any(item => item.Id == t.Id && TournamentStore.CanView(item, actor, admin)));
    }

    [Theory]
    [InlineData("registered")]
    [InlineData("waitlist")]
    [InlineData("active")]
    [InlineData("withdrawn")]
    [InlineData("removed")]
    public void 参赛记录不会授予已取消赛事的访问权限(string entrantStatus)
    {
        var t = Create();
        Command(t.Id, "register", "p0", deck: Deck("参赛卡组"));
        Assert.True(TournamentStore.CanView(_store.Read(t.Id), "p0", false));
        Command(t.Id, "cancel", configure: r => r.Reason = "取消测试赛");
        _store.SystemUpdate(t.Id, state => { state.Entrants[0].Status = entrantStatus; return true; });

        Assert.False(TournamentStore.CanView(_store.Read(t.Id), "p0", false));
        var error = Assert.Throws<InvalidOperationException>(() => Command(t.Id, "withdraw", "p0"));
        Assert.Equal("没有查看本场赛事的权限。", error.Message);
    }

    [Fact]
    public void 重复报名请求不能绕过取消后的赛事访问权限()
    {
        var t = Create();
        var request = new TournamentCommand { Id = t.Id, Action = "register", RequestId = "register-once", Revision = t.Revision };
        _store.Execute("p0", "参赛者", false, request, Deck("参赛卡组"), Now);
        Command(t.Id, "cancel", configure: r => r.Reason = "取消测试赛");

        var error = Assert.Throws<InvalidOperationException>(() => _store.Execute("p0", "参赛者", false, request, Deck("参赛卡组"), Now));
        Assert.Equal("没有查看本场赛事的权限。", error.Message);
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("swiss")]
    [InlineData("cut")]
    [InlineData("completed")]
    public void 未取消的公开赛事仍对普通玩家可见(string status)
    {
        var t = new Tournament { Status = status, Owner = "host" };
        Assert.True(TournamentStore.CanView(t, "other", false));
    }

    [Fact]
    public void 报名后可在开赛一天前签到且没有确认说明时拒绝签到()
    {
        var t = Create(start: Now + 86_400_000);
        Command(t.Id, "register", "p0", deck: Deck("参赛卡组"));
        var error = Assert.Throws<InvalidOperationException>(() => Command(t.Id, "checkin", "p0", r => r.ConfirmCheckin = false));
        Assert.Contains("签到后将不能退出", error.Message);
        Assert.False(_store.Read(t.Id).Entrants[0].CheckedIn);
        var checkedIn = Command(t.Id, "checkin", "p0");
        Assert.True(checkedIn.Entrants[0].CheckedIn);
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "checkin", "p0", at: checkedIn.StartsAt));
    }

    [Fact]
    public void 未签到选手可退出但已签到选手换卡组及赛事重审后仍不能退出()
    {
        var t = Create(start: Now + 86_400_000);
        Command(t.Id, "register", "p0", deck: Deck("原卡组"));
        Assert.Equal("removed", Command(t.Id, "withdraw", "p0").Entrants[0].Status);
        Command(t.Id, "register", "p0", deck: Deck("原卡组"));
        Command(t.Id, "checkin", "p0");
        Assert.Contains("已签到", Assert.Throws<InvalidOperationException>(() => Command(t.Id, "withdraw", "p0")).Message);
        var changed = Command(t.Id, "register", "p0", deck: Deck("新卡组"));
        Assert.True(changed.Entrants[0].CheckedIn);
        Assert.Equal("新卡组", changed.Entrants[0].Deck.Name);
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "withdraw", "p0"));
        var edit = Application(start: Now + 172_800_000);
        edit.Action = "edit"; edit.Id = t.Id; edit.Revision = changed.Revision;
        _store.Execute("host", "举办者", false, edit, now: Now);
        Command(t.Id, "approve", "admin", admin: true);
        var restored = new TournamentStore(Path.Combine(_root, "tournaments.db")).Read(t.Id);
        Assert.True(restored.Entrants[0].CheckedIn);
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "withdraw", "p0"));
    }

    [Fact]
    public void 已签到候补不能退出且瑞士轮和淘汰赛选手也不能主动退赛()
    {
        var waiting = Create();
        for (var i = 0; i < 9; i++) Command(waiting.Id, "register", "w" + i, deck: Deck("卡组"));
        var checkedIn = Command(waiting.Id, "checkin", "w8");
        Assert.Equal("waitlist", checkedIn.Entrants[8].Status);
        Assert.Throws<InvalidOperationException>(() => Command(waiting.Id, "withdraw", "w8"));
        var t = Ready();
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "withdraw", "p0"));
        for (var i = 0; i < 3; i++) { NaturalFinish(t.Id); if (i < 2) Command(t.Id, "openRound", at: Start); }
        Command(t.Id, "openRound", at: Start);
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "withdraw", "p0"));
    }

    [Fact]
    public void 赛事协议提供按账号区分的联系信息且未绑定不会伪造QQ()
    {
        var t = Ready();
        TournamentContact[] contacts = [new("p0", "同名选手", "123456789"), new("p1", "同名选手", "234567890"), new("p2", "测试账号", null)];
        var response = GrandUMI.WebSocketBridge.BuildTournamentResponse(t, "p0", false, "contacts-test", contacts: contacts);
        var wire = response.GetProperty("tournament").GetProperty("contacts");
        Assert.Equal("p0", wire[0].GetProperty("account").GetString());
        Assert.Equal("123456789", wire[0].GetProperty("qq").GetString());
        Assert.Equal("234567890", wire[1].GetProperty("qq").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, wire[2].GetProperty("qq").ValueKind);
    }

    [Fact]
    public void 报名满额后进入候补且仅补入已签到候补()
    {
        var t = Create();
        for (var i = 0; i < 10; i++) Command(t.Id, "register", "p" + i, deck: Deck("卡组"));
        Assert.Equal(2, _store.Read(t.Id).Entrants.Count(e => e.Status == "waitlist"));
        for (var i = 1; i <= 8; i++) Command(t.Id, "checkin", "p" + i, at: Start - 600_000);
        var opened = Command(t.Id, "openRound", at: Start);
        Assert.Equal("removed", opened.Entrants.First(e => e.Account == "p0").Status);
        Assert.Equal("active", opened.Entrants.First(e => e.Account == "p8").Status);
        Assert.Equal("waitlist", opened.Entrants.First(e => e.Account == "p9").Status);
        Assert.Equal(4, opened.CurrentRound!.Tables.Count);
    }

    [Fact]
    public void 开赛前五分钟系统释放未签到正选并补入已签到候补且不重复修改状态()
    {
        var t = Create();
        for (var i = 0; i < 9; i++) Command(t.Id, "register", "p" + i, deck: Deck("卡组"));
        for (var i = 1; i < 9; i++) Command(t.Id, "checkin", "p" + i, at: Start - 600_000);
        Assert.False(_store.SystemUpdate(t.Id, state => TournamentStore.ReconcileCheckins(state, Start - 300_001)));
        Assert.True(_store.SystemUpdate(t.Id, state => TournamentStore.ReconcileCheckins(state, Start - 300_000)));
        var released = _store.Read(t.Id);
        Assert.Equal("removed", released.Entrants.First(e => e.Account == "p0").Status);
        Assert.Equal("registered", released.Entrants.First(e => e.Account == "p8").Status);
        Assert.False(_store.SystemUpdate(t.Id, state => TournamentStore.ReconcileCheckins(state, Start - 299_000)));
    }

    [Fact]
    public void 未满员签到时不得开赛也不得降为其他人数()
    {
        var t = Create();
        for (var i = 0; i < 7; i++) { Command(t.Id, "register", "p" + i, deck: Deck("卡组")); Command(t.Id, "checkin", "p" + i, at: Start - 600_000); }
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "openRound", at: Start));
        Assert.Empty(_store.Read(t.Id).Rounds);
    }

    [Fact]
    public void 同桌两名玩家落座即可开赛且其他桌继续等待()
    {
        var t = Ready(); var table = t.CurrentRound!.Tables[0];
        var first = Command(t.Id, "seat", table.Player0!, r => r.TableId = table.Id, Start + 1000);
        Assert.Equal("waiting", first.CurrentRound!.Tables[0].Status);
        var second = Command(t.Id, "seat", table.Player1!, r => r.TableId = table.Id, Start + 2000);
        Assert.Equal("starting", second.CurrentRound!.Tables[0].Status);
        Assert.Equal(Start + 2000, second.CurrentRound.Tables[0].StartedAt);
        Assert.All(second.CurrentRound.Tables.Skip(1), m => { Assert.Equal("waiting", m.Status); Assert.Null(m.StartedAt); });
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "seat", "outsider", r => r.TableId = table.Id, Start + 3000));
    }

    [Fact]
    public void 管理员主动结束必须提供二次确认令牌且全桌进入人工确认()
    {
        var t = Ready();
        _store.SystemUpdate(t.Id, state => { TournamentStore.Finish(state.CurrentRound!.Tables[0], "p0", "已正常结束", "system", Start); return true; });
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "endRound", configure: r => r.Reason = "结束"));
        var ended = ForceEnd(t.Id);
        Assert.Equal("manual", ended.CurrentRound!.Status);
        Assert.All(ended.CurrentRound.Tables, m => Assert.False(m.Confirmed));
        Assert.Equal("p0", ended.CurrentRound.Tables[0].OriginalOutcome);
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "seat", ended.CurrentRound.Tables[1].Player0!, r => r.TableId = ended.CurrentRound.Tables[1].Id, Start + 2000));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "openRound", at: Start + 2000));
    }

    [Fact]
    public void 逐桌确认全部完成后仍等待管理员主动开启下一轮()
    {
        var t = Ready(); var ended = ForceEnd(t.Id);
        for (var i = 0; i < ended.CurrentRound!.Tables.Count; i++)
        {
            var table = ended.CurrentRound.Tables[i];
            Command(t.Id, "settle", configure: r => { r.TableId = table.Id; r.Outcome = "p0"; r.Reason = "确认甲方获胜"; });
            if (i < ended.CurrentRound.Tables.Count - 1) Assert.Throws<InvalidOperationException>(() => Command(t.Id, "openRound", at: Start));
        }
        var settled = _store.Read(t.Id);
        Assert.Equal("settled", settled.CurrentRound!.Status); Assert.Single(settled.Rounds);
        Assert.Equal(4, TournamentStore.Standings(settled).Sum(s => s.Points));
        var next = Command(t.Id, "openRound", at: Start + 600_000);
        Assert.Equal(2, next.Rounds.Count);
    }

    [Fact]
    public void 非管理人员不能结束或结算且结果必须合法()
    {
        var t = Ready();
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "endRound", "p0", r => { r.ConfirmationToken = TournamentStore.EndToken(t, "p0"); r.Reason = "结束"; }));
        var ended = ForceEnd(t.Id); var table = ended.CurrentRound!.Tables[0];
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "settle", table.Player0!, r => { r.TableId = table.Id; r.Outcome = "p0"; r.Reason = "选择"; }));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "settle", configure: r => { r.TableId = table.Id; r.Outcome = "bye"; r.Reason = "不能伪造轮空"; }));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "settle", configure: r => { r.TableId = table.Id; r.Outcome = "p0"; }));
    }

    [Fact]
    public void 重复请求只结算一次且旧版本不能覆盖新状态()
    {
        var t = Ready(); var ended = ForceEnd(t.Id);
        var command = new TournamentCommand { Action = "settle", Id = t.Id, RequestId = "once", Revision = ended.Revision, TableId = ended.CurrentRound!.Tables[0].Id, Outcome = "p0", Reason = "确认甲方" };
        var first = _store.Execute("manager", "管理", false, command, now: Now);
        var second = _store.Execute("manager", "管理", false, command, now: Now);
        Assert.Equal(first.Revision, second.Revision); Assert.Single(second.Audit.Where(a => a.Action == "settle"));
        command.RequestId = "new-request";
        Assert.Throws<InvalidOperationException>(() => _store.Execute("manager", "管理", false, command, now: Now));
    }

    [Fact]
    public void 人工结束后迟到的自然结果不得替代逐桌人工确认()
    {
        var t = Ready();
        _store.SystemUpdate(t.Id, state => { state.CurrentRound!.Tables[0].RoomId = "late-room"; return true; });
        ForceEnd(t.Id);
        TournamentRuntime.RecordResult(_store, "late-room", 0, "迟到结果", DateTime.UtcNow);
        var table = _store.Read(t.Id).CurrentRound!.Tables[0];
        Assert.False(table.Confirmed); Assert.Equal("manual", table.Status);
        Command(t.Id, "settle", configure: r => { r.TableId = table.Id; r.Outcome = "p1"; r.Reason = "裁判确认乙方获胜"; });
        TournamentRuntime.RecordResult(_store, "late-room", 0, "重复迟到结果", DateTime.UtcNow);
        Assert.Equal("p1", _store.Read(t.Id).CurrentRound!.Tables[0].Outcome);
    }

    [Fact]
    public void 未决申诉阻止推进并只能由独立裁判处理()
    {
        var t = Ready(); NaturalFinish(t.Id); var table = t.CurrentRound!.Tables[0];
        Command(t.Id, "appeal", table.Player0!, r => { r.TableId = table.Id; r.Reason = "需要检查对局结果"; });
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "openRound", at: Start));
        var appeal = _store.Read(t.Id).Appeals.Single();
        Command(t.Id, "resolveAppeal", "judge", r => { r.AppealId = appeal.Id; r.Reason = "已核对对局记录"; });
        Assert.Equal(2, Command(t.Id, "openRound", at: Start).Rounds.Count);
    }

    [Fact]
    public void 正常瑞士轮不重复配对并按排名进入固定淘汰签表()
    {
        var t = Ready(); var pairs = new HashSet<string>();
        for (var round = 0; round < 3; round++)
        {
            foreach (var table in _store.Read(t.Id).CurrentRound!.Tables)
                Assert.True(pairs.Add(string.Join(':', new[] { table.Player0!, table.Player1! }.Order())));
            NaturalFinish(t.Id);
            if (round != 2) Command(t.Id, "openRound", at: Start);
        }
        var rankings = TournamentStore.Standings(_store.Read(t.Id));
        var cut = Command(t.Id, "openRound", at: Start);
        Assert.Equal("cut", cut.CurrentRound!.Stage); Assert.Equal(rankings[0].Account, cut.CurrentRound.Tables[0].Player0);
        Assert.Equal(rankings[3].Account, cut.CurrentRound.Tables[0].Player1);
        Assert.Equal(rankings[1].Account, cut.CurrentRound.Tables[1].Player0);
        NaturalFinish(t.Id); Command(t.Id, "openRound", at: Start); NaturalFinish(t.Id);
        var complete = _store.Read(t.Id); Assert.Equal("completed", complete.Status); Assert.NotNull(complete.Champion);
        Assert.Equal(2, TournamentStore.Standings(complete).Count(s => s.Placement == 3));
    }

    [Fact]
    public void 缺席退出保留战绩并在奇数下一轮安排一个轮空()
    {
        var t = Ready(); NaturalFinish(t.Id);
        var before = TournamentStore.Standings(_store.Read(t.Id)).First(s => s.Account == "p0").Points;
        _store.SystemUpdate(t.Id, state => { state.Entrants.First(e => e.Account == "p0").Status = "withdrawn"; return true; });
        var next = Command(t.Id, "openRound", at: Start);
        Assert.Single(next.CurrentRound!.Tables.Where(m => m.Outcome == "bye"));
        Assert.DoesNotContain(next.CurrentRound.Tables, m => m.Player0 == "p0" || m.Player1 == "p0");
        Assert.Equal(before, TournamentStore.Standings(next).First(s => s.Account == "p0").Points);
    }

    [Fact]
    public void 人工结算进度和锁定卡组可从数据库完整恢复()
    {
        var t = Ready(); var ended = ForceEnd(t.Id);
        Command(t.Id, "settle", configure: r => { r.TableId = ended.CurrentRound!.Tables[0].Id; r.Outcome = "doubleLoss"; r.Reason = "双方超时"; });
        var reloaded = new TournamentStore(Path.Combine(_root, "tournaments.db")).Read(t.Id);
        Assert.Equal("manual", reloaded.CurrentRound!.Status); Assert.Single(reloaded.CurrentRound.Tables.Where(m => m.Confirmed));
        Assert.Equal(51, reloaded.Entrants[0].DeckRaw.Split('\n').Length); Assert.Equal("pinned-rules", reloaded.RulesetId);
        Assert.All(TournamentStore.Standings(reloaded), s => Assert.True(s.Omw >= .33 && s.Oomw >= .33));
    }

    [Fact]
    public void 同一选手不能参加同时进行的其他赛事且工作人员不能参赛()
    {
        var first = Ready(); var second = Create();
        Assert.Throws<InvalidOperationException>(() => Command(second.Id, "register", "manager", deck: Deck("卡组")));
        for (var i = 0; i < 8; i++) { Command(second.Id, "register", "p" + i, deck: Deck("卡组")); Command(second.Id, "checkin", "p" + i, at: Start - 600_000); }
        Assert.Throws<InvalidOperationException>(() => Command(second.Id, "openRound", at: Start));
        Assert.Equal("swiss", _store.Read(first.Id).Status);
    }

    [Fact]
    public void 真实协议字段与前端契约一致且普通玩家不能读取其他卡组或抽签种子()
    {
        var t = Ready();
        var response = GrandUMI.WebSocketBridge.BuildTournamentResponse(t, "p0", false, "request-test");
        Assert.Equal("request-test", response.GetProperty("requestId").GetString());
        var view = response.GetProperty("tournament");
        Assert.Equal(t.Id, view.GetProperty("id").GetString());
        Assert.Equal(t.Revision, view.GetProperty("revision").GetInt64());
        Assert.Equal("swiss", view.GetProperty("rounds")[0].GetProperty("stage").GetString());
        Assert.False(view.GetProperty("rounds")[0].GetProperty("tables")[0].GetProperty("confirmed").GetBoolean());
        Assert.All(view.GetProperty("entrants").EnumerateArray(), e => Assert.Equal(System.Text.Json.JsonValueKind.Null, e.GetProperty("deckRaw").ValueKind));
        Assert.False(view.TryGetProperty("drawSeed", out _));
        Assert.False(view.GetProperty("canManage").GetBoolean());
        var judgeView = GrandUMI.WebSocketBridge.BuildTournamentResponse(t, "judge", false, "judge-request");
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, judgeView.GetProperty("tournament").GetProperty("entrants")[0].GetProperty("deckRaw").ValueKind);
    }
}
