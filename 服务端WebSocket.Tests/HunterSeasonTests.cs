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
    [Fact]
    public void 公开称号读取遵守佩戴所有权且不会创建其他玩家排位资料()
    {
        var store = TestHonorStore(true);
        store.SelectFaction("释迦", "管理员", "east", Now);
        GrantTestTitles("释迦");
        GrantTestTitles("普通玩家");
        Assert.Null(store.GetPublicEquippedSeasonTitle("释迦"));
        store.EquipSeasonTitle("释迦", "S1 五老星");
        var titles = store.GetPublicEquippedSeasonTitles(new[] { "释迦", "普通玩家", "不存在" });
        Assert.Equal("S1 五老星", titles["释迦"]);
        Assert.Null(titles["普通玩家"]);
        Assert.Null(titles["不存在"]);
        Assert.Null(TestHonorStore(false).GetPublicEquippedSeasonTitle("释迦"));
        store.EquipSeasonTitle("释迦", null);
        Assert.Null(store.GetPublicEquippedSeasonTitle("释迦"));
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM rank_profiles;";
        Assert.Equal(1L, command.ExecuteScalar());
    }
    private RankedStore TestHonorStore(bool enabled, bool standard = true)
        => new(_path, null, null, standard, RankedBountySettlementMode.HuntersSeasonTwo, enabled);
    private static readonly string[] AllTestTitles =
        ["S1 海贼王", "S1 四皇", "S1 海军元帅", "S1 海军大将", "S1 世界之王", "S1 五老星"];
    private void GrantTestTitles(string account)
    {
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(account.Trim().ToUpperInvariant()))).ToLowerInvariant();
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        foreach (var title in AllTestTitles)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO rank_admin_test_honors VALUES('S1',$key,$title,$now);";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$now", Now.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 测试管理员拥有六种称号_个人资料和榜单一致_重启保留且不重复(bool standard)
    {
        var store = TestHonorStore(true, standard);
        store.SelectFaction("释迦", "管理员", "east", Now);
        GrantTestTitles("释迦"); GrantTestTitles("释迦");
        Sql("INSERT INTO rank_season_honors SELECT 'S1',account_key,'S1 海贼王',1,updated_at_utc FROM rank_profiles WHERE display_name='管理员';");
        Assert.True(store.TryRefreshLeaderboardSnapshot(Now));
        var snapshot = store.GetSnapshot("释迦", "管理员", Now);
        Assert.Equal(AllTestTitles.Order(StringComparer.Ordinal), snapshot.Profile.SeasonTitles);
        Assert.Equal(snapshot.Profile.SeasonTitles, Assert.Single(snapshot.Leaderboard).SeasonTitles);
        Assert.Equal(0, snapshot.Profile.RankPoints);
        Assert.Equal(snapshot.Profile.SeasonTitles, TestHonorStore(true, standard).GetProfileSnapshot("释迦", "管理员", Now).SeasonTitles);
        using var wire = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(RankWire.Profile(snapshot.Profile)));
        Assert.Equal(6, wire.RootElement.GetProperty("seasonTitles").GetArrayLength());
    }

    [Fact]
    public void 测试称号开关默认关闭_真实赛季荣誉仍正常显示()
    {
        var store = TestHonorStore(false);
        store.SelectFaction("栗子", "管理员", "north", Now);
        GrantTestTitles("栗子");
        Sql("INSERT INTO rank_season_honors SELECT 'S1',account_key,'S1 海军大将',2,updated_at_utc FROM rank_profiles WHERE display_name='管理员';");
        Assert.True(store.TryRefreshLeaderboardSnapshot(Now));
        var snapshot = store.GetSnapshot("栗子", "管理员", Now);
        Assert.Equal(new[] { "S1 海军大将" }, snapshot.Profile.SeasonTitles);
        Assert.Equal(snapshot.Profile.SeasonTitles, Assert.Single(snapshot.Leaderboard).SeasonTitles);
    }

    [Fact]
    public void 普通账号和类似管理员名称不能获得测试荣誉()
    {
        var store = TestHonorStore(true);
        foreach (var account in new[] { "普通玩家", "释迦测试", "释迦2号" })
        {
            store.SelectFaction(account, account, "south", Now);
            GrantTestTitles(account);
        }
        Assert.True(store.TryRefreshLeaderboardSnapshot(Now));
        Assert.Empty(store.GetProfileSnapshot("普通玩家", "普通玩家", Now).SeasonTitles);
        Assert.Empty(store.GetProfileSnapshot("释迦测试", "释迦测试", Now).SeasonTitles);
        Assert.Equal(6, store.GetProfileSnapshot("释迦2号", "释迦2号", Now).SeasonTitles.Count);
        var leaderboard = store.GetSnapshot("普通玩家", "普通玩家", Now).Leaderboard;
        Assert.All(leaderboard.Where(item => item.DisplayName != "释迦2号"), item => Assert.Empty(item.SeasonTitles));
    }
    private RankedStore Prepare()
    {
        var store = Store();
        store.SelectFaction("a", "甲", "east", Now);
        store.SelectFaction("b", "乙", "west", Now);
        return store;
    }

    [Fact]
    public void 六枚称号可单选切换取消_重启保存_快照立即更新且不改积分钱包()
    {
        var store = TestHonorStore(true);
        store.SelectFaction("释迦", "管理员", "east", Now);
        GrantTestTitles("释迦");
        var before = store.GetSnapshot("释迦", "管理员", Now);
        var balance = store.GetChatDecorationExchangeSnapshot("释迦", "管理员", Now).BalanceBerries;
        Assert.Null(before.Profile.EquippedSeasonTitle);
        store.EquipSeasonTitle("释迦", "S1 海贼王");
        var king = store.GetSnapshot("释迦", "管理员", Now);
        Assert.Equal("S1 海贼王", king.Profile.EquippedSeasonTitle);
        Assert.Equal("S1 海贼王", Assert.Single(king.Leaderboard).EquippedSeasonTitle);
        Assert.True(king.SnapshotVersion > before.SnapshotVersion);
        Assert.Equal(before.GeneratedAtUtc, king.GeneratedAtUtc);
        store.EquipSeasonTitle("释迦", "S1 四皇");
        Assert.Equal("S1 四皇", TestHonorStore(true).GetProfileSnapshot("释迦", "管理员", Now).EquippedSeasonTitle);
        Assert.Null(TestHonorStore(false).GetProfileSnapshot("释迦", "管理员", Now).EquippedSeasonTitle);
        var emperor = store.GetSnapshot("释迦", "管理员", Now);
        Assert.Equal("S1 四皇", Assert.Single(emperor.Leaderboard).EquippedSeasonTitle);
        Assert.Equal(6, emperor.Profile.SeasonTitles.Count);
        Assert.Equal(before.Profile.RankPoints, emperor.Profile.RankPoints);
        Assert.Equal(before.Profile.Games, emperor.Profile.Games);
        Assert.Equal(balance, store.GetChatDecorationExchangeSnapshot("释迦", "管理员", Now).BalanceBerries);
        using var wire = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(RankWire.Profile(emperor.Profile)));
        Assert.Equal("S1 四皇", wire.RootElement.GetProperty("equippedSeasonTitle").GetString());
        store.EquipSeasonTitle("释迦", null);
        Assert.Null(store.GetSnapshot("释迦", "管理员", Now).Profile.EquippedSeasonTitle);
        Assert.Null(Assert.Single(store.GetSnapshot("释迦", "管理员", Now).Leaderboard).EquippedSeasonTitle);
        Assert.Null(TestHonorStore(true).GetProfileSnapshot("释迦", "管理员", Now).EquippedSeasonTitle);
    }

    [Fact]
    public void 普通玩家不能伪造佩戴_重复选择和重复取消不改变版本()
    {
        var store = TestHonorStore(true);
        store.SelectFaction("释迦", "管理员", "east", Now);
        store.SelectFaction("b", "乙", "west", Now);
        GrantTestTitles("释迦");
        Assert.Throws<SeasonTitleValidationException>(() => store.EquipSeasonTitle("b", "S1 海贼王"));
        Assert.Throws<SeasonTitleValidationException>(() => store.EquipSeasonTitle("释迦", "S2 海贼王"));
        store.EquipSeasonTitle("释迦", "S1 海贼王");
        var before = store.GetSnapshot("释迦", "管理员", Now).SnapshotVersion;
        store.EquipSeasonTitle("释迦", "S1 海贼王");
        Assert.Equal(before, store.GetSnapshot("释迦", "管理员", Now).SnapshotVersion);
        store.EquipSeasonTitle("释迦", null);
        var canceled = store.GetSnapshot("释迦", "管理员", Now).SnapshotVersion;
        store.EquipSeasonTitle("释迦", null);
        Assert.Equal(canceled, store.GetSnapshot("释迦", "管理员", Now).SnapshotVersion);
        Assert.Null(store.GetProfileSnapshot("b", "乙", Now).EquippedSeasonTitle);
    }

    [Fact]
    public void 标准狂野合并拥有称号_共用选择_各自榜单与积分保持独立()
    {
        var wildPath = _path + ".wild";
        try
        {
            var standard = new RankedStore(_path, null, null, true, RankedBountySettlementMode.HuntersSeasonTwo,
                otherSeasonTitleDatabasePath: wildPath);
            var wild = new RankedStore(wildPath, null, null, false, RankedBountySettlementMode.HuntersSeasonTwo,
                seasonTitleEquipmentDatabasePath: _path, otherSeasonTitleDatabasePath: _path);
            standard.SelectFaction("a", "甲", "east", Now);
            wild.SelectFaction("a", "甲", "west", Now);
            Sql("INSERT INTO rank_season_honors SELECT 'S1',account_key,'S1 四皇',2,updated_at_utc FROM rank_profiles WHERE season_id='S2';");
            using (var connection = new SqliteConnection($"Data Source={wildPath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO rank_season_honors SELECT 'S1',account_key,'S1 海贼王',1,updated_at_utc FROM rank_profiles WHERE season_id='S2';";
                command.ExecuteNonQuery();
            }
            var standardBefore = standard.GetSnapshot("a", "甲", Now);
            var wildBefore = wild.GetSnapshot("a", "甲", Now);
            standard.EquipSeasonTitle("a", "S1 海贼王");
            Assert.Equal(2, standard.GetProfileSnapshot("a", "甲", Now).SeasonTitles.Count);
            Assert.Equal(2, wild.GetProfileSnapshot("a", "甲", Now).SeasonTitles.Count);
            var wildAfter = wild.GetSnapshot("a", "甲", Now);
            Assert.Equal("S1 海贼王", Assert.Single(wildAfter.Leaderboard).EquippedSeasonTitle);
            Assert.True(wildAfter.SnapshotVersion > wildBefore.SnapshotVersion);
            wild.EquipSeasonTitle("a", "S1 四皇");
            Assert.Equal("S1 四皇", Assert.Single(standard.GetSnapshot("a", "甲", Now).Leaderboard).EquippedSeasonTitle);
            Assert.Equal("east", standard.GetProfileSnapshot("a", "甲", Now).Faction);
            Assert.Equal("west", wild.GetProfileSnapshot("a", "甲", Now).Faction);
            Assert.Equal(standardBefore.Profile.RankPoints, standard.GetProfileSnapshot("a", "甲", Now).RankPoints);
            Assert.Equal(wildBefore.Profile.RankPoints, wild.GetProfileSnapshot("a", "甲", Now).RankPoints);
        }
        finally { SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(wildPath + suffix); }
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
