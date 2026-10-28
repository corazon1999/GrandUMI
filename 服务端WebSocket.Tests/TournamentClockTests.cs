using System.Text.Json;
using GrandUMI.Game.Tournaments;
using Xunit;

namespace GrandUMI.Tests;

public sealed partial class TournamentStoreTests
{
    private Tournament PlayingTournament()
    {
        var t = Ready();
        _store.SystemUpdate(t.Id, state => {
            foreach (var table in state.CurrentRound!.Tables) { table.Status = "playing"; table.StartedAt = Start; }
            return true;
        });
        return _store.Read(t.Id);
    }

    [Fact]
    public void 决赛及旧存档默认不限时且普通淘汰赛仍有独立时限()
    {
        var final = JsonSerializer.Deserialize<TournamentRound>("""{"stage":"cut","status":"live","tables":[{"status":"playing","startedAt":1000}]}""", TournamentStore.JsonOptions)!;
        Assert.Null(TournamentStore.TableDeadline(final, final.Tables[0]));
        var clock = GrandUMI.WebSocketBridge.BuildTournamentClock(new Tournament { Name = "决赛" }, final, final.Tables[0], [], Start);
        Assert.True(clock.GetProperty("isFinal").GetBoolean());
        Assert.Equal(JsonValueKind.Null, clock.GetProperty("deadline").ValueKind);
        final.Tables.Add(new() { StartedAt = 1000 });
        Assert.Equal(1_801_000, TournamentStore.TableDeadline(final, final.Tables[0]));
    }

    [Fact]
    public void 管理员可独立缩短延长取消计时且数据库恢复后保持剩余截止时间()
    {
        var t = PlayingTournament(); var first = t.CurrentRound!.Tables[0];
        var changed = Command(t.Id, "adjustTableTime", configure: r => { r.TableId = first.Id; r.RemainingSeconds = 300; r.Reason = "调整本桌剩余时间"; }, at: Start + 600_000);
        Assert.Equal(Start + 900_000, TournamentStore.TableDeadline(changed.CurrentRound!, changed.CurrentRound!.Tables[0]));
        Assert.Equal(Start + 1_800_000, TournamentStore.TableDeadline(changed.CurrentRound!, changed.CurrentRound.Tables[1]));
        var restored = new TournamentStore(Path.Combine(_root, "tournaments.db")).Read(t.Id);
        Assert.Equal(Start + 900_000, TournamentStore.TableDeadline(restored.CurrentRound!, restored.CurrentRound!.Tables[0]));
        changed = Command(t.Id, "adjustTableTime", configure: r => { r.TableId = first.Id; r.RemainingSeconds = 1200; r.Reason = "延长本桌时间"; }, at: Start + 700_000);
        Assert.Equal(Start + 1_900_000, TournamentStore.TableDeadline(changed.CurrentRound!, changed.CurrentRound!.Tables[0]));
        changed = Command(t.Id, "adjustTableTime", configure: r => { r.TableId = first.Id; r.RemainingSeconds = 0; r.Reason = "改为不限时"; }, at: Start + 800_000);
        Assert.Null(TournamentStore.TableDeadline(changed.CurrentRound!, changed.CurrentRound!.Tables[0]));
        Assert.False(changed.CurrentRound.Tables[0].Confirmed);
        Assert.Equal(3, changed.Audit.Count(a => a.Action == "adjustTableTime"));
    }

    [Fact]
    public void 超时只持久化一次提醒且延时后可以再次提醒而不写入胜负()
    {
        var t = PlayingTournament(); var table = t.CurrentRound!.Tables[0];
        var overdue = Start + 1_800_000;
        Assert.True(_store.SystemUpdate(t.Id, value => TournamentStore.MarkTableOverdue(value, table.Id, overdue)));
        var notified = _store.Read(t.Id);
        Assert.Equal("playing", notified.CurrentRound!.Tables[0].Status);
        Assert.Null(notified.CurrentRound.Tables[0].Outcome); Assert.False(notified.CurrentRound.Tables[0].Confirmed);
        Assert.All(TournamentStore.Standings(notified), standing => { Assert.Equal(0, standing.Wins); Assert.Equal(0, standing.Losses); });
        Assert.False(_store.SystemUpdate(t.Id, value => TournamentStore.MarkTableOverdue(value, table.Id, overdue + 60_000)));
        Assert.Equal(notified.Revision, _store.Read(t.Id).Revision);
        Assert.Equal(overdue, new TournamentStore(Path.Combine(_root, "tournaments.db")).Read(t.Id).CurrentRound!.Tables[0].TimeoutNotifiedAt);
        Command(t.Id, "adjustTableTime", configure: r => { r.TableId = table.Id; r.RemainingSeconds = 60; r.Reason = "继续比赛一分钟"; }, at: overdue);
        Assert.False(_store.SystemUpdate(t.Id, value => TournamentStore.MarkTableOverdue(value, table.Id, overdue + 59_999)));
        Assert.True(_store.SystemUpdate(t.Id, value => TournamentStore.MarkTableOverdue(value, table.Id, overdue + 60_000)));
        Assert.Equal(2, _store.Read(t.Id).Audit.Count(a => a.Action == "timeExpired"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void 落座超时不会自动判负且之后仍可重新落座(bool firstSeated)
    {
        var t = Ready(); var table = t.CurrentRound!.Tables[0];
        _store.SystemUpdate(t.Id, value => { value.CurrentRound!.Tables[0].Seated0 = firstSeated; return true; });
        Assert.True(_store.SystemUpdate(t.Id, value => TournamentStore.MarkTableOverdue(value, table.Id, Start + 300_000)));
        var blocked = _store.Read(t.Id).CurrentRound!.Tables[0];
        Assert.Equal("blocked", blocked.Status); Assert.False(blocked.Confirmed); Assert.Null(blocked.Outcome);
        Assert.All(_store.Read(t.Id).Entrants, entrant => Assert.Equal("active", entrant.Status));
        Command(t.Id, "seat", table.Player0!, r => r.TableId = table.Id, Start + 600_000);
        var seated = Command(t.Id, "seat", table.Player1!, r => r.TableId = table.Id, Start + 601_000).CurrentRound!.Tables[0];
        Assert.Equal("starting", seated.Status); Assert.Null(seated.TimeoutNotifiedAt);
    }

    [Fact]
    public void 单桌时间调整检查权限原因范围旧版本和请求幂等()
    {
        var t = PlayingTournament(); var table = t.CurrentRound!.Tables[0];
        foreach (var actor in new[] { "host", "judge", table.Player0! })
            Assert.Throws<InvalidOperationException>(() => Command(t.Id, "adjustTableTime", actor, r => { r.TableId = table.Id; r.RemainingSeconds = 600; r.Reason = "越权调整"; }));
        foreach (int? seconds in new int?[] { null, -1, 86_401 })
            Assert.Throws<InvalidOperationException>(() => Command(t.Id, "adjustTableTime", configure: r => { r.TableId = table.Id; r.RemainingSeconds = seconds; r.Reason = "非法范围"; }));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "adjustTableTime", configure: r => { r.TableId = table.Id; r.RemainingSeconds = 600; }));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "adjustTableTime", table.Player0!, r => { r.TableId = table.Id; r.RemainingSeconds = 600; r.Reason = "不能修改自己的桌"; }, admin: true));
        var command = new TournamentCommand { Id = t.Id, Action = "adjustTableTime", RequestId = "clock-once", Revision = t.Revision, TableId = table.Id, RemainingSeconds = 600, Reason = "确认本桌时间" };
        var changed = _store.Execute("manager", "管理", false, command, now: Start);
        Assert.Equal(changed.Revision, _store.Execute("manager", "管理", false, command, now: Start + 60_000).Revision);
        command.RequestId = "stale-clock";
        Assert.Throws<InvalidOperationException>(() => _store.Execute("manager", "管理", false, command, now: Start));
    }

    [Theory]
    [InlineData("p0")] [InlineData("p1")] [InlineData("doubleLoss")]
    public void 进行中的桌可人工决定胜负或双败并拒绝迟到自然结果覆盖(string outcome)
    {
        var t = PlayingTournament(); var table = t.CurrentRound!.Tables[0];
        _store.SystemUpdate(t.Id, value => { value.CurrentRound!.Tables[0].RoomId = "manual-decision"; return true; });
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "adjudicate", "judge", r => { r.TableId = table.Id; r.Outcome = outcome; r.Reason = "越权"; }));
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "adjudicate", configure: r => { r.TableId = table.Id; r.Outcome = outcome; }));
        var changed = Command(t.Id, "adjudicate", configure: r => { r.TableId = table.Id; r.Outcome = outcome; r.Reason = "管理员现场判定"; });
        Assert.Equal(outcome, changed.CurrentRound!.Tables[0].Outcome); Assert.Equal("manager", changed.CurrentRound.Tables[0].ConfirmedBy);
        Assert.False(changed.CurrentRound.Tables[1].Confirmed);
        Assert.Throws<InvalidOperationException>(() => Command(t.Id, "openRound"));
        TournamentRuntime.RecordResult(_store, "manual-decision", 0, "迟到终局", DateTime.UtcNow);
        Assert.Equal(outcome, _store.Read(t.Id).CurrentRound!.Tables[0].Outcome);
    }

    [Fact]
    public void 淘汰赛人工双败保留空签位并允许无冠军结束()
    {
        var t = Ready();
        for (var i = 0; i < 3; i++) { NaturalFinish(t.Id); if (i < 2) Command(t.Id, "openRound", at: Start); }
        var cut = Command(t.Id, "openRound", at: Start);
        foreach (var table in cut.CurrentRound!.Tables)
            Command(t.Id, "adjudicate", configure: r => { r.TableId = table.Id; r.Outcome = "doubleLoss"; r.Reason = "双方违规，人工判双败"; });
        var finished = _store.Read(t.Id);
        Assert.Equal("completed", finished.Status); Assert.Null(finished.Champion);
        Assert.All(finished.CurrentRound!.Tables, table => Assert.Equal("manager", table.ConfirmedBy));
    }
}
