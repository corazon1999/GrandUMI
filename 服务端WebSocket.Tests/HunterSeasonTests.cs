using GrandUMI.Game.Ranked;
using GrandUMI.Game;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrandUMI.Tests;

public sealed class HunterSeasonTests : IDisposable
{
    private readonly string _path;
    private static readonly DateTime Now = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
    public HunterSeasonTests()
    {
        var root = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT")
            ?? throw new InvalidOperationException("先通过 GrandUmiTemp.ps1 设置测试临时目录。");
        if (!Path.GetFullPath(root).StartsWith(@"E:\GrandUMI-Temp\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("测试目录必须位于 E:\\GrandUMI-Temp\\。");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, $"hunter-{Guid.NewGuid():N}.db");
    }
    private RankedStore Store() => new(_path);
    private RankedStore Prepare()
    {
        var store = Store();
        store.SelectFaction("a", "甲", "east", Now);
        store.SelectFaction("b", "乙", "west", Now);
        return store;
    }
    private static RankedMatchSettlement Win(RankedStore store, int index, int winner = 0)
        => Assert.IsType<RankedMatchSettlement>(store.RecordMatch($"battle-{index}", Now.AddMinutes(index), "a", "甲", "b", "乙", winner));
    private void Sql(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }

    [Fact]
    public void 连胜从第三局递增_失败不扣分且中断_平局不结算_重复请求不计分()
    {
        var store = Prepare();
        Assert.Equal(1, Win(store, 1).Player0.RankPointDelta);
        Assert.Equal(1, Win(store, 2).Player0.RankPointDelta);
        Assert.Equal(2, Win(store, 3).Player0.RankPointDelta);
        Assert.Null(store.RecordMatch("draw", Now.AddMinutes(3.5), "a", "甲", "b", "乙", -1));
        Assert.Equal(3, Win(store, 4).Player0.RankPointDelta);
        Assert.Equal(4, Win(store, 5).Player0.RankPointDelta);
        var lost = Win(store, 6, 1);
        Assert.Equal(0, lost.Player0.RankPointDelta);
        Assert.Equal(11, lost.Player0.RankPointsAfter);
        Assert.Equal(2, lost.Player1.WinStreakEndedBounty);
        Assert.Equal(1, lost.Player1.RankDifferenceAdjustment);
        Assert.Equal(4, lost.Player1.RankPointDelta);
        Assert.Null(store.RecordMatch("battle-6", Now.AddMinutes(6), "a", "甲", "b", "乙", 1));
        Assert.Equal(1, Win(store, 7).Player0.RankPointDelta);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 2)]
    public void 终结奖励以对手赛前三连胜为门槛(int streak, int bonus)
    {
        var store = Prepare();
        for (var i = 1; i <= streak; i++) Win(store, i);
        Assert.Equal(bonus, Win(store, 10, 1).Player1.WinStreakEndedBounty);
    }

    [Fact]
    public void 领先海域按实时累计总分而非单人分_非零并列有效_全零无奖励()
    {
        var store = Prepare();
        Assert.Equal(0, Win(store, 1).Player0.RankDifferenceAdjustment);
        store.SelectFaction("c", "丙", "west", Now);
        Sql("UPDATE rank_profiles SET rank_points=100 WHERE season_id='S2' AND display_name='丙';");
        Assert.Equal(1, Win(store, 2).Player0.RankDifferenceAdjustment);
        Sql("UPDATE rank_profiles SET rank_points=100 WHERE season_id='S2' AND display_name='甲';");
        var third = Win(store, 3).Player0;
        Assert.Equal(1, third.RankDifferenceAdjustment);
        Assert.Equal(1, third.StreakAdjustment);
        Assert.Equal(3, third.RankPointDelta);
    }

    [Fact]
    public void 无定级无倍率从零开始_海域锁定_标准狂野独立_协议带四海和赛季荣誉()
    {
        var store = Prepare();
        var profile = store.GetProfileSnapshot("a", "甲", Now);
        Assert.Equal("S2", profile.SeasonId); Assert.Equal(0, profile.RankPoints);
        Assert.Equal(0, profile.PlacementRequired); Assert.Equal("见习猎人", profile.Tier);
        Assert.Equal("east", store.SelectFaction("a", "甲", "north", Now, true)!.Profile.Faction);
        Assert.Null(store.SelectFaction("a", "甲", "pirate", Now));
        var wildPath = _path + ".wild";
        try
        {
            var wild = new RankedStore(wildPath, null, null, false);
            Assert.Null(wild.GetProfileSnapshot("a", "甲", Now).Faction);
            Assert.Equal("south", wild.SelectFaction("a", "甲", "south", Now)!.Profile.Faction);
            Assert.Throws<ChatDecorationValidationException>(() => wild.GetChatDecorationExchangeSnapshot("a", "甲", Now));
        }
        finally { SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(wildPath + suffix); }
        var json = System.Text.Json.JsonSerializer.Serialize(RankWire.Profile(profile));
        Assert.Contains("\"seasonTitles\":[]", json); Assert.Contains("\"faction\":\"east\"", json);
    }

    [Theory]
    [InlineData("pirate", true)]
    [InlineData("marine", true)]
    [InlineData("government", true)]
    [InlineData("east", false)]
    [InlineData("west", false)]
    [InlineData("south", false)]
    [InlineData("north", false)]
    [InlineData(null, false)]
    public void 更新前创建的赏金排位身份不会误计入猎人人头(string? faction, bool oldSeason)
    {
        var identity = faction is null ? null : new PlayerRankIdentity(faction, "称号", null, 5, 0);
        Assert.Equal(oldSeason, GameRoomManager.IsBountySeasonRankIdentity(identity));
    }

    [Theory]
    [InlineData(0, "见习猎人")]
    [InlineData(9, "见习猎人")]
    [InlineData(10, "十人斩")]
    [InlineData(99, "十人斩")]
    [InlineData(100, "百人斩")]
    [InlineData(999, "百人斩")]
    [InlineData(1000, "千人斩")]
    [InlineData(9999, "千人斩")]
    [InlineData(10000, "万人斩")]
    public void 四档段位边界(int heads, string tier) => Assert.Equal((tier, (int?)null), RankedStore.RankLabel(heads, "east"));

    [Fact]
    public void 十一月最后一天北京时间结束_之后固定S2并冻结人头()
    {
        var store = Prepare();
        var end = store.GetProfileSnapshot("a", "甲", Now).SeasonEndsAtUtc;
        Assert.Equal(new DateTime(2026, 11, 30, 16, 0, 0, DateTimeKind.Utc), end);
        var last = store.RecordMatch("last", end.AddTicks(-1), "a", "甲", "b", "乙", 0)!;
        Assert.Equal(1, last.Player0.RankPointDelta);
        var stopped = store.RecordMatch("stopped", end, "a", "甲", "b", "乙", 0)!;
        Assert.Equal(0, stopped.Player0.RankPointDelta); Assert.False(stopped.Player0.RankPointFormulaApplied);
        var future = Store().GetProfileSnapshot("a", "甲", end.AddYears(1));
        Assert.Equal("S2", future.SeasonId); Assert.Equal(1, future.RankPoints);
    }

    [Fact]
    public async Task 两实例并发同一局只结算一次()
    {
        Prepare();
        var results = await Task.WhenAll(Task.Run(() => Store().RecordMatch("parallel", Now, "a", "甲", "b", "乙", 0)),
            Task.Run(() => Store().RecordMatch("parallel", Now, "a", "甲", "b", "乙", 0)));
        Assert.Single(results.Where(r => r is not null));
        Assert.Equal(1, Store().GetProfileSnapshot("a", "甲", Now).RankPoints);
    }

    [Fact]
    public void S1荣誉按各阵营前列永久固化_背包消费和装备原样保留_隐分继承()
    {
        var old = new RankedStore(_path, null, null, true, RankedBountySettlementMode.Enabled);
        var s1 = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        foreach (var faction in new[] { "pirate", "marine", "government" })
            for (var i = 1; i <= 7; i++) old.SelectFaction($"{faction}{i}", $"{faction}{i}", faction, s1);
        Sql("UPDATE rank_profiles SET placement_games=5,rank_points=1000,highest_rank_points=1000,rating=1700 WHERE season_id='S1';" +
            "UPDATE rank_profiles SET rank_points=1100,highest_rank_points=1200 WHERE display_name IN ('pirate1','marine1','government1');");
        var purchased = old.PurchaseChatDecoration("pirate1", "pirate1", "quote-pirate-king-man", "purchase-s1", ChatDecorationCatalog.PurchasePriceBerries, s1);
        Assert.True(purchased.Succeeded);
        old.EquipChatDecoration("pirate1", "pirate1", "quote-pirate-king-man", "opening", "equipment-s1", s1);
        var wallet = old.GetChatDecorationExchangeSnapshot("pirate1", "pirate1", s1);
        var hidden = old.GetMatchRating("pirate1", "pirate1", s1);
        var hunter = Store();
        Assert.Equal(new[] { "S1 海贼王" }, hunter.GetProfileSnapshot("pirate1", "pirate1", Now).SeasonTitles);
        Assert.Equal(new[] { "S1 海军元帅" }, hunter.GetProfileSnapshot("marine1", "marine1", Now).SeasonTitles);
        Assert.Equal(new[] { "S1 世界之王" }, hunter.GetProfileSnapshot("government1", "government1", Now).SeasonTitles);
        Assert.Equal(hidden, hunter.GetMatchRating("pirate1", "pirate1", Now));
        using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM rank_season_honors;"; Assert.Equal(15L, command.ExecuteScalar());
            command.CommandText = "SELECT COUNT(*) FROM rank_season_honors WHERE title='S1 四皇';"; Assert.Equal(4L, command.ExecuteScalar());
            command.CommandText = "SELECT COUNT(*) FROM rank_season_honors WHERE title='S1 海军大将';"; Assert.Equal(3L, command.ExecuteScalar());
            command.CommandText = "SELECT COUNT(*) FROM rank_season_honors WHERE title='S1 五老星';"; Assert.Equal(5L, command.ExecuteScalar());
            command.CommandText = "SELECT COUNT(*) FROM rank_exchange_wallets WHERE season_id='S1';"; Assert.Equal(21L, command.ExecuteScalar());
        }
        // 结算后改变旧排名、重新初始化都不得重新授予荣誉或补回已消费余额。
        Sql("UPDATE rank_profiles SET rank_points=1 WHERE season_id='S1' AND display_name='pirate1';");
        var restarted = Store();
        var after = restarted.GetChatDecorationExchangeSnapshot("pirate1", "pirate1", Now);
        Assert.Equal(wallet.BalanceBerries, after.BalanceBerries); Assert.Equal(wallet.Items.Select(i => (i.Definition.Id, i.Owned, string.Join(",", i.EquippedSlots))), after.Items.Select(i => (i.Definition.Id, i.Owned, string.Join(",", i.EquippedSlots))));
        Assert.Equal("S1", after.SeasonId);
        Assert.Equal(new[] { "S1 海贼王" }, restarted.GetProfileSnapshot("pirate1", "pirate1", Now).SeasonTitles);
        restarted.SelectFaction("pirate1", "pirate1", "east", Now);
        restarted.SelectFaction("marine1", "marine1", "west", Now);
        restarted.RecordMatch("new-season", Now, "pirate1", "pirate1", "marine1", "marine1", 0);
        Assert.Equal(wallet.BalanceBerries, restarted.GetChatDecorationExchangeSnapshot("pirate1", "pirate1", Now).BalanceBerries);
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
    }
}
