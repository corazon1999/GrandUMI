using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>纯抽牌及抽牌后弃牌型生命【触发】回归测试。</summary>
public class LifeTriggerSimpleCompletionTests
{
    public static IEnumerable<object[]> DrawCases()
    {
        yield return ["EB01-060", 2, 1];
        yield return ["EB02-056", 1, 0];
        yield return ["EB04-041", 2, 1];
        yield return ["EB04-059", 2, 1];
        yield return ["EB04-060", 2, 1];
        yield return ["OP05-094", 2, 1];
        yield return ["OP06-116", 1, 0];
        yield return ["OP08-115", 2, 1];
        yield return ["OP09-059", 1, 0];
        yield return ["OP10-109", 2, 1];
        yield return ["OP10-116", 2, 1];
        yield return ["OP11-079", 1, 0];
        yield return ["OP13-117", 1, 0];
        yield return ["OP14-019", 1, 0];
        yield return ["OP14-057", 2, 0];
        yield return ["OP14-116", 1, 0];
        yield return ["ST29-017", 2, 1];
    }

    [Theory]
    [MemberData(nameof(DrawCases))]
    public async Task LifeTrigger_WithEnoughDeck_DrawsAndDiscardsAsPrinted(
        string sourceNumber, int drawCount, int discardCount)
    {
        var state = TestScene.New()
            .MyDeckTop("OP15-003", "OP15-004", "OP15-005")
            .Build();
        var source = Card(sourceNumber);

        await EffectRuntime.Resolve(state, 0, source,
            EffectTrigger.OnLifeRevealTrigger, new MockPromptService());

        Assert.Equal(3 - drawCount, state.Players[0].Deck.Count);
        Assert.Equal(drawCount - discardCount, state.Players[0].Hand.Count);
        Assert.Equal(discardCount, state.Players[0].Trash.Count);
    }

    [Theory]
    [MemberData(nameof(DrawCases))]
    public async Task LifeTrigger_WithEmptyDeck_DoesNotCreateCards(string sourceNumber, int _, int discardCount)
    {
        _ = discardCount;
        var state = TestScene.New().Build();
        var source = Card(sourceNumber);

        await EffectRuntime.Resolve(state, 0, source,
            EffectTrigger.OnLifeRevealTrigger, new MockPromptService());

        Assert.Empty(state.Players[0].Hand);
        Assert.Empty(state.Players[0].Trash);
        Assert.True(state.IsGameOver);
        Assert.Equal(1, state.WinnerIndex);
    }

    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };
}
