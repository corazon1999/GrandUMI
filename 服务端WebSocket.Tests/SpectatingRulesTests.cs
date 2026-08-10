using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class SpectatingRulesTests
{
    [Theory]
    [InlineData("open", false, null, null, true)]
    [InlineData("closed", true, null, null, false)]
    [InlineData("friends", false, null, null, false)]
    [InlineData("friends", true, null, null, true)]
    [InlineData("password", false, "123456", "654321", false)]
    [InlineData("password", false, "123456", "123456", true)]
    public void 观战模式按好友和观战码校验(
        string mode,
        bool isFriend,
        string? expectedCode,
        string? providedCode,
        bool expectedAllowed)
    {
        var result = SpectatingRules.CheckAccess(mode, isFriend, expectedCode, providedCode, wasKicked: false);
        Assert.Equal(expectedAllowed, result.Allowed);
    }

    [Fact]
    public void 被踢出的账号不能再次进入本局()
    {
        var result = SpectatingRules.CheckAccess("open", true, null, null, wasKicked: true);
        Assert.False(result.Allowed);
        Assert.Contains("无法再次观战", result.Error);
    }

    [Fact]
    public void 观战码始终是六位数字()
    {
        for (var i = 0; i < 100; i++)
            Assert.Matches("^[0-9]{6}$", SpectatingRules.GenerateCode());
    }
}
