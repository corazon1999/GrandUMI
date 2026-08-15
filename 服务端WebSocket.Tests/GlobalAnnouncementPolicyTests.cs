using Xunit;

namespace GrandUMI.Tests;

public sealed class GlobalAnnouncementPolicyTests
{
    [Fact]
    public void OnlyConfiguredAccountsCanUseAdministratorFeatures()
    {
        Assert.True(GlobalAnnouncementPolicy.IsAuthorized("释迦"));
        Assert.True(GlobalAnnouncementPolicy.IsAuthorized("栗子"));
        Assert.False(GlobalAnnouncementPolicy.IsAuthorized("释迦 "));
        Assert.False(GlobalAnnouncementPolicy.IsAuthorized("栗子 "));
        Assert.False(GlobalAnnouncementPolicy.IsAuthorized("管理员"));
        Assert.False(GlobalAnnouncementPolicy.IsAuthorized(null));
    }

    [Fact]
    public void AnnouncementContentIsTrimmedAndCapped()
    {
        Assert.Equal("服务器维护通知", GlobalAnnouncementPolicy.Normalize("  服务器维护通知  "));
        Assert.Null(GlobalAnnouncementPolicy.Normalize("   "));
        Assert.Equal(GlobalAnnouncementPolicy.MaximumContentLength,
            GlobalAnnouncementPolicy.Normalize(new string('a', GlobalAnnouncementPolicy.MaximumContentLength + 1))!.Length);
    }

    [Fact]
    public void RankedWinStreakAnnouncementStartsAtThreeWins()
    {
        Assert.Null(GlobalAnnouncementPolicy.FormatRankedWinStreak(
            "爱丽丝", "marine", "海军少将", 2));
        Assert.Equal("爱丽丝 打飞了“海军阵营”的海军少将，完成了三连胜！",
            GlobalAnnouncementPolicy.FormatRankedWinStreak(
                "爱丽丝", "marine", "海军少将", 3));
        Assert.Equal("爱丽丝 打飞了“海贼阵营”的船长，完成了八连胜！",
            GlobalAnnouncementPolicy.FormatRankedWinStreak(
                "爱丽丝", "pirate", "船长", 8));
        Assert.Equal("爱丽丝 打飞了“世界政府阵营”的浅海契约，完成了十一连胜！",
            GlobalAnnouncementPolicy.FormatRankedWinStreak(
                "爱丽丝", "government", "浅海契约", 11));
    }

    [Fact]
    public void RankedWinStreakEndedAnnouncementStartsAtThreeWins()
    {
        Assert.Null(GlobalAnnouncementPolicy.FormatRankedWinStreakEnded(
            "爱丽丝", 2, "marine", "卡普"));
        Assert.Equal("爱丽丝的五连胜 被 海军阵营 的卡普 终结了",
            GlobalAnnouncementPolicy.FormatRankedWinStreakEnded(
                " 爱丽丝 ", 5, "marine", " 卡普 "));
        Assert.Equal("玩家的十一连胜 被 世界政府阵营 的玩家 终结了",
            GlobalAnnouncementPolicy.FormatRankedWinStreakEnded(
                null, 11, "government", " "));
    }
}
