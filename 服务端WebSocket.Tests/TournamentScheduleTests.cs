using GrandUMI.Game.Tournaments;
using Xunit;

namespace GrandUMI.Tests;

public sealed partial class TournamentStoreTests
{
    [Theory]
    [InlineData(8, 3, 4)] [InlineData(16, 4, 4)] [InlineData(32, 5, 8)]
    [InlineData(64, 6, 8)] [InlineData(128, 7, 8)] [InlineData(256, 8, 16)]
    public void 各人数档位的推荐赛程保持原规格(int capacity, int rounds, int cut)
    {
        var request = Application(capacity);
        request.SwissRounds = rounds; request.CutSize = cut;
        var t = _store.Execute("host", "举办者", false, request, now: Now);
        var saved = new TournamentStore(Path.Combine(_root, "tournaments.db")).Read(t.Id);
        Assert.Equal(rounds, saved.SwissRounds); Assert.Equal(cut, saved.CutSize);
        Assert.Equal(rounds, saved.ConfiguredSwissRounds); Assert.Equal(cut, saved.ConfiguredCutSize);
        Assert.Equal(cut, Tournament.RecommendedCutSize(capacity));
    }

    [Theory]
    [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public void 自定义轮次在未满员开赛后持久化且不因退赛缩减(int rounds)
    {
        var request = Application(); request.SwissRounds = rounds; request.CutSize = 8;
        var t = _store.Execute("host", "举办者", false, request, now: Now);
        Command(t.Id, "approve", "admin", admin: true);
        for (var i = 0; i < 3; i++)
        {
            Command(t.Id, "register", "p" + i, deck: Deck("测试卡组"));
            Command(t.Id, "checkin", "p" + i);
        }
        t = Command(t.Id, "openRound");
        Assert.Equal(rounds, t.SwissRounds); Assert.Equal(2, t.CutSize);
        Assert.Equal(3, t.StartedEntrantCount); Assert.Equal(8, t.ConfiguredCutSize);
        Command(t.Id, "withdraw", "p0", configure: r => r.ConfirmWithdrawal = true);
        var saved = new TournamentStore(Path.Combine(_root, "tournaments.db")).Read(t.Id);
        Assert.Equal(rounds, saved.SwissRounds); Assert.Equal(2, saved.CutSize);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(8)] [InlineData(16)] [InlineData(32)]
    public void 所有出轮档位可完成瑞士轮到冠军的赛程(int cut)
    {
        var request = Application(32); request.SwissRounds = 3; request.CutSize = cut;
        var t = _store.Execute("host", "举办者", false, request, now: Now);
        Command(t.Id, "approve", "admin", admin: true);
        for (var i = 0; i < 32; i++)
        {
            Command(t.Id, "register", "p" + i, deck: Deck("测试卡组"));
            Command(t.Id, "checkin", "p" + i);
        }
        Command(t.Id, "openRound");
        for (var r = 0; r < 3; r++)
        {
            NaturalFinish(t.Id);
            if (r < 2) Command(t.Id, "openRound");
        }
        var leader = TournamentStore.Standings(_store.Read(t.Id)).First().Account;
        t = Command(t.Id, "openRound");
        if (cut == 1)
        {
            Assert.Equal("completed", t.Status); Assert.Equal(leader, t.Champion);
            Assert.Equal(3, t.Rounds.Count); Assert.All(t.Rounds, r => Assert.Equal("swiss", r.Stage));
            Assert.Equal(31, t.Entrants.Count(e => e.Status == "eliminated"));
        }
        else
        {
            Assert.Equal("cut", t.CurrentRound!.Stage); Assert.Equal(cut / 2, t.CurrentRound.Tables.Count);
            while (t.Status != "completed")
            {
                NaturalFinish(t.Id); t = _store.Read(t.Id);
                if (t.Status != "completed") t = Command(t.Id, "openRound");
            }
            Assert.NotNull(t.Champion);
        }
        Assert.Equal(1, TournamentStore.Standings(t).Single(s => s.Account == t.Champion).Placement);
    }

    [Fact]
    public void 九轮小规模赛事重复交手仍须裁判确认且可正常完成()
    {
        var request = Application(); request.SwissRounds = 9; request.CutSize = 1;
        var t = _store.Execute("host", "举办者", false, request, now: Now);
        Command(t.Id, "approve", "admin", admin: true);
        for (var i = 0; i < 2; i++)
        {
            Command(t.Id, "register", "p" + i, deck: Deck("测试卡组"));
            Command(t.Id, "checkin", "p" + i);
        }
        Command(t.Id, "openRound");
        for (var r = 1; r <= 9; r++)
        {
            Assert.Equal(r, _store.Read(t.Id).CurrentRound!.Number);
            NaturalFinish(t.Id);
            if (r == 9) break;
            Assert.Contains("重复交手", Assert.Throws<InvalidOperationException>(() => Command(t.Id, "openRound")).Message);
            Command(t.Id, "openRound", configure: c => { c.AllowRepeat = true; c.Reason = "自定义九轮赛，裁判确认重复配对"; });
        }
        Assert.Equal("completed", Command(t.Id, "openRound").Status);
    }

    [Theory]
    [InlineData(0, 4)] [InlineData(2, 4)] [InlineData(10, 4)]
    [InlineData(3, 0)] [InlineData(3, 3)] [InlineData(3, 64)] [InlineData(3, 16)]
    public void 申请和修改均拒绝非法赛程且不改写已有配置(int rounds, int cut)
    {
        var invalid = Application(); invalid.SwissRounds = rounds; invalid.CutSize = cut;
        Assert.Throws<InvalidOperationException>(() => _store.Execute("host", "举办者", false, invalid, now: Now));
        Assert.Empty(_store.ReadAll());
        var valid = Application(); valid.SwissRounds = 7; valid.CutSize = 2;
        var t = _store.Execute("host", "举办者", false, valid, now: Now);
        invalid.Action = "edit"; invalid.Id = t.Id; invalid.Revision = t.Revision;
        Assert.Throws<InvalidOperationException>(() => _store.Execute("host", "举办者", false, invalid, now: Now));
        var saved = _store.Read(t.Id);
        Assert.Equal(7, saved.SwissRounds); Assert.Equal(2, saved.CutSize); Assert.Equal(t.Revision, saved.Revision);
    }

    [Fact]
    public void 修改申请保存自定义赛程并重新审核()
    {
        var t = Create();
        var edit = Application(64); edit.Action = "edit"; edit.Id = t.Id; edit.Revision = t.Revision;
        edit.SwissRounds = 9; edit.CutSize = 32;
        t = _store.Execute("host", "举办者", false, edit, now: Now);
        Assert.Equal("pending", t.Status); Assert.Equal(64, t.Capacity);
        t = Command(t.Id, "approve", "admin", admin: true);
        Assert.Equal(9, t.SwissRounds); Assert.Equal(32, t.CutSize);
    }
}
