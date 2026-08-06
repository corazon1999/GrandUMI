using GrandUMI.Cards;
using Xunit;

namespace GrandUMI.Tests;

public class KeywordNormalizerTests
{
    private static CardInfo Card(params string[] keywords) => new()
    {
        Number = "TEST-001",
        Name = "测试卡",
        Color = "红",
        Kind = CardKind.Character,
        Property = "特",
        Keywords = keywords,
    };

    [Theory]
    [InlineData("白胡子海贼团", "白胡子海盗团")]
    [InlineData("白胡子海盗团", "白胡子海贼团")]
    [InlineData("红发海賊団", "红发海盗团")]
    [InlineData("黑胡子海賊團", "黑胡子海盗团")]
    [InlineData("九蛇海盜團", "九蛇海盗团")]
    [InlineData("　〈白胡子海贼团〉 ", "白胡子海盗团")]
    public void HasKeyword_NormalizesTranslationAndTypographyVariants(string actual, string expected)
    {
        Assert.True(Card(actual).HasKeyword(expected));
    }

    [Theory]
    [InlineData("原白胡子海贼团", "白胡子海盗团")]
    [InlineData("白胡子海盗团旗下", "白胡子海贼团")]
    [InlineData("原〈白胡子海賊団〉", "白胡子海盗团")]
    public void HasKeywordContaining_MatchesContainedCanonicalKeyword(string actual, string expected)
    {
        Assert.True(Card(actual).HasKeywordContaining(expected));
    }

    [Fact]
    public void KeywordMatching_DoesNotMatchDifferentPirateCrews()
    {
        var card = Card("黑胡子海贼团");

        Assert.False(card.HasKeyword("白胡子海盗团"));
        Assert.False(card.HasKeywordContaining("白胡子海盗团"));
    }
}
