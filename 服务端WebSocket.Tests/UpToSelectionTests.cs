using GrandUMI.Effects;
using Xunit;

namespace GrandUMI.Tests;

public class UpToSelectionTests
{
    [Fact]
    public void 最多N张提示_统一允许选择零到N张()
    {
        var range = PromptSystem.NormalizeChooseRange("OwnCharacter", "将我方最多 5 张角色转为活跃状态", 5, 5);

        Assert.Equal(0, range.Min);
        Assert.Equal(5, range.Max);
    }

    [Theory]
    [InlineData("ReorderToDeckBottom", "将剩余卡牌自选顺序放回卡组最下方")]
    [InlineData("OwnHandDiscard", "选择最多 2 张手牌作为成本")]
    public void 排序或成本提示_不会被放宽为可跳过(string kind, string text)
    {
        var extra = text.Contains("成本", StringComparison.Ordinal)
            ? new Dictionary<string, object?> { ["isCost"] = true }
            : null;
        var range = PromptSystem.NormalizeChooseRange(kind, text, 2, 2, extra);

        Assert.Equal(2, range.Min);
        Assert.Equal(2, range.Max);
    }
}
