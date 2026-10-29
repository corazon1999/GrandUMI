using GrandUMI.Game.Ranked;
using Xunit;

namespace GrandUMI.Tests;

public sealed class StandardRankedCardPoolSelectionTests
{
    [Theory]
    [InlineData(null, false, true, "real")]
    [InlineData(null, true, true, "preview")]
    [InlineData("real", false, true, "real")]
    [InlineData("real", true, false, null)]
    [InlineData("preview", false, true, "preview")]
    [InlineData("preview", true, true, "preview")]
    [InlineData("all", false, true, "all")]
    [InlineData("all", true, true, "all")]
    [InlineData(" PREVIEW ", true, true, "preview")]
    [InlineData("", false, false, null)]
    [InlineData("invalid", false, false, null)]
    [InlineData("invalid", true, false, null)]
    public void 入队环境选择_保留旧客户端兼容_拒绝无效选择和实卡中的新卡(
        string? requested, bool usesPreviewCards, bool expectedOk, string? expectedPool)
    {
        var ok = StandardRankedCardPoolPolicy.TryResolve(requested, usesPreviewCards, out var pool, out var error);
        Assert.Equal(expectedOk, ok);
        if (ok)
        {
            Assert.Null(error);
            Assert.Equal(expectedPool, StandardRankedCardPoolPolicy.ToProtocol(pool));
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
            if (requested == "real") Assert.Contains("实卡环境不能使用", error);
        }
    }
}
