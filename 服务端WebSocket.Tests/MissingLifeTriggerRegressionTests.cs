using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>卡图有【触发】但历史运行数据/效果缺失的批量回归测试。</summary>
public class MissingLifeTriggerRegressionTests
{
    public static IEnumerable<object[]> PrintedTriggerCards()
    {
        foreach (var number in new[]
        {
            "EB01-039", "EB02-030", "OP05-038", "OP05-039", "OP08-037", "OP08-038",
            "OP08-053", "OP08-056", "OP08-068", "OP08-075", "OP08-091", "OP08-094",
            "OP08-095", "OP08-096", "OP08-097", "OP08-104", "OP08-105", "OP08-111",
            "OP08-112", "OP08-113", "OP08-114", "OP10-100", "OP12-075", "OP14-089",
            "ST10-016", "ST10-017", "ST12-002", "ST12-016", "ST14-016", "ST22-016",
        })
            yield return [number];
    }

    [Theory]
    [MemberData(nameof(PrintedTriggerCards))]
    public void PrintedTriggerCard_HasRuntimeTriggerData(string number)
    {
        _ = TestScene.New().Build();

        var trigger = CardDatabase.Get(number)!.Trigger;

        Assert.StartsWith("【触发】", trigger);
    }

    [Theory]
    [InlineData("EB04-029")]
    [InlineData("ST10-015")]
    public void CounterOnlyCard_DoesNotHaveRuntimeTriggerData(string number)
    {
        _ = TestScene.New().Build();

        Assert.True(string.IsNullOrEmpty(CardDatabase.Get(number)!.Trigger));
    }

    [Fact]
    public async Task OP13_113_OnlyOffersCardsWithRealPrintedTrigger()
    {
        var state = TestScene.New()
            .MyDeckTop("EB02-030", "EB04-029", "ST10-015")
            .Build();
        var me = state.Players[0];
        var source = Card("OP13-113");
        me.Characters.Add(source);
        var valid = me.Deck[0];
        var prompts = new MockPromptService().QueueChoose(valid.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("LilithReveal", prompt.kind);
        Assert.Equal([valid.Id.ToString()], prompt.choices);
        Assert.Contains(valid, me.Hand);
    }

    [Theory]
    [InlineData("EB02-030")]
    [InlineData("OP08-037")]
    [InlineData("OP08-053")]
    public async Task DrawOneLifeTrigger_DrawsOneCard(string number)
    {
        var state = TestScene.New().MyDeckTop("OP15-003").Build();

        await ResolveLife(state, Card(number));

        Assert.Single(state.Players[0].Hand);
        Assert.Empty(state.Players[0].Deck);
    }

    [Fact]
    public async Task OP08_105_DrawsTwoThenDiscardsOne()
    {
        var state = TestScene.New().MyDeckTop("OP15-003", "OP15-004").Build();

        await ResolveLife(state, Card("OP08-105"));

        Assert.Single(state.Players[0].Hand);
        Assert.Single(state.Players[0].Trash);
        Assert.Empty(state.Players[0].Deck);
    }

    [Theory]
    [InlineData("OP08-075")]
    [InlineData("ST10-017")]
    public async Task ActiveDonLifeTrigger_AddsOneActiveDon(string number)
    {
        var state = TestScene.New().Build();
        state.Players[0].DonDeck.Add(new DonCard { State = DonState.InDeck });

        await ResolveLife(state, Card(number));

        Assert.Equal(1, state.Players[0].ActiveDonCount);
        Assert.Empty(state.Players[0].DonDeck);
    }

    [Fact]
    public async Task OP08_038_RestsOpponentCostThreeCharacter()
    {
        var state = TestScene.New().OppCharacter("EB01-005").Build();
        var target = state.Players[1].Characters[0];

        await ResolveLife(state, Card("OP08-038"),
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.True(target.IsTapped);
    }

    [Theory]
    [InlineData("OP08-091")]
    [InlineData("OP08-097")]
    public async Task CostThreeKoLifeTrigger_KOsOpponentCharacter(string number)
    {
        var state = TestScene.New().OppCharacter("EB01-005").Build();
        var target = state.Players[1].Characters[0];

        await ResolveLife(state, Card(number),
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.Empty(state.Players[1].Characters);
        Assert.Contains(target, state.Players[1].Trash);
    }

    [Fact]
    public async Task OP08_095_BuffsChosenLeaderForTurn()
    {
        var state = TestScene.New().Build();
        var leader = state.Players[0].Leader;

        await ResolveLife(state, Card("OP08-095"),
            new MockPromptService().QueueChoose(leader.Id.ToString()));

        Assert.Equal(2000, leader.PowerModThisTurn);
    }

    [Fact]
    public async Task OP08_096_PlaysBlackCostThreeCharacterFromTrash()
    {
        var state = TestScene.New().Build();
        var target = Card("EB01-042");
        state.Players[0].Trash.Add(target);

        await ResolveLife(state, Card("OP08-096"),
            new MockPromptService().QueueChoose(target.Id.ToString()));

        Assert.Contains(target, state.Players[0].Characters);
        Assert.DoesNotContain(target, state.Players[0].Trash);
    }

    [Fact]
    public async Task OP08_056_PlaysItselfIntoStageArea()
    {
        var state = TestScene.New().Build();
        var source = Card("OP08-056");
        state.Players[0].Trash.Add(source);

        await ResolveLife(state, source);

        Assert.Same(source, state.Players[0].StageCard);
        Assert.DoesNotContain(source, state.Players[0].Trash);
    }

    [Fact]
    public async Task OP08_068_ReturnsOneDonAndPlaysItself()
    {
        var state = TestScene.New().MyActiveDon(1).Build();
        var source = Card("OP08-068");
        state.Players[0].Trash.Add(source);

        await ResolveLife(state, source);

        Assert.Contains(source, state.Players[0].Characters);
        Assert.Empty(state.Players[0].CostArea);
        Assert.Single(state.Players[0].DonDeck);
    }

    [Fact]
    public async Task OP08_104_DiscardsOne_PlaysItself_ThenDrawsOne()
    {
        var state = TestScene.New().MyHandAdd("OP15-003").MyDeckTop("OP15-004").Build();
        var source = Card("OP08-104");
        var discard = state.Players[0].Hand[0];
        state.Players[0].Trash.Add(source);

        await ResolveLife(state, source,
            new MockPromptService().QueueChoose(discard.Id.ToString()));

        Assert.Contains(source, state.Players[0].Characters);
        Assert.Contains(discard, state.Players[0].Trash);
        Assert.Single(state.Players[0].Hand);
        Assert.Empty(state.Players[0].Deck);
    }

    [Theory]
    [InlineData("OP08-111")]
    [InlineData("OP08-114")]
    public async Task EggheadConditionalTrigger_DiscardsAndPlaysItself(string number)
    {
        var state = TestScene.New().MyHandAdd("OP15-003").Build();
        AddLife(state, 0, 2);
        var source = Card(number);
        var discard = state.Players[0].Hand[0];
        state.Players[0].Trash.Add(source);

        await ResolveLife(state, source,
            new MockPromptService().QueueChoose(discard.Id.ToString()));

        Assert.Contains(source, state.Players[0].Characters);
        Assert.Contains(discard, state.Players[0].Trash);
    }

    [Fact]
    public async Task OP08_113_Discards_PlaysItself_AndKOsCostThreeCharacter()
    {
        var state = TestScene.New().MyHandAdd("OP15-003").OppCharacter("EB01-005").Build();
        AddLife(state, 0, 2);
        var source = Card("OP08-113");
        var discard = state.Players[0].Hand[0];
        var target = state.Players[1].Characters[0];
        state.Players[0].Trash.Add(source);
        var prompts = new MockPromptService()
            .QueueChoose(discard.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await ResolveLife(state, source, prompts);

        Assert.Contains(source, state.Players[0].Characters);
        Assert.Contains(target, state.Players[1].Trash);
        Assert.Empty(state.Players[1].Characters);
    }

    [Fact]
    public async Task OP10_100_WithRevolutionaryLeaderAndFiveTotalLife_PlaysItself()
    {
        var state = TestScene.New("OP05-001").Build();
        AddLife(state, 0, 2);
        AddLife(state, 1, 3);
        var source = Card("OP10-100");
        state.Players[0].Trash.Add(source);

        await ResolveLife(state, source);

        Assert.Contains(source, state.Players[0].Characters);
    }

    [Fact]
    public async Task ST10_016_BuffsLeaderThroughNextOwnTurn()
    {
        var state = TestScene.New().Build();
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 2;
        var leader = state.Players[0].Leader;

        await ResolveLife(state, Card("ST10-016"),
            new MockPromptService().QueueChoose(leader.Id.ToString()));

        Assert.Equal(1000, state.ContinuousPowerBonus(0, leader));
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        Assert.Equal(1000, state.ContinuousPowerBonus(0, leader));
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 4;
        Assert.Equal(0, state.ContinuousPowerBonus(0, leader));
    }

    private static Task ResolveLife(
        GameState state,
        CardInstance source,
        MockPromptService? prompts = null)
        => EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnLifeRevealTrigger,
            prompts ?? new MockPromptService());

    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    private static void AddLife(GameState state, int playerIndex, int count)
    {
        for (int i = 0; i < count; i++)
            state.Players[playerIndex].LifeArea.Add(Card("OP15-003"));
    }
}
