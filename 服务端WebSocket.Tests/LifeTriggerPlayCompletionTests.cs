using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>登场、区域交换、咚!!操作及废弃区回收型生命【触发】回归测试。</summary>
public class LifeTriggerPlayCompletionTests
{
    public static IEnumerable<object[]> PlayAndMoveCards()
    {
        foreach (var number in new[]
        {
            "EB01-026", "EB01-035", "EB01-038", "EB04-027", "OP02-089",
            "OP02-090", "OP02-091", "OP03-119", "OP05-115", "OP06-057",
            "OP07-078", "OP09-107", "OP10-080", "OP11-081", "OP14-018",
            "OP14-082", "OP14-117", "OP14-118", "OP16-101", "OP16-117",
            "ST13-017", "ST13-018", "ST36-002",
        })
            yield return [number];
    }

    [Theory]
    [MemberData(nameof(PlayAndMoveCards))]
    public async Task LifeTrigger_WithLegalCardsOrCost_CompletesMovement(string sourceNumber)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var opp = state.Players[1];
        var source = Card(sourceNumber);
        CardInstance? selected = null;

        switch (sourceNumber)
        {
            case "EB01-026":
            case "ST13-017":
            case "ST13-018":
                me.LifeArea.Add(Card("OP15-003"));
                selected = Card("OP15-004");
                me.Hand.Add(selected);
                break;
            case "EB01-035":
                me.Trash.Add(source);
                me.CostArea.Add(new DonCard { State = DonState.Active });
                break;
            case "EB01-038":
                me.CostArea.Add(new DonCard { State = DonState.Active });
                me.Deck.AddRange([Card("OP15-003"), Card("OP15-004")]);
                break;
            case "EB04-027":
            case "OP03-119":
            case "OP14-118":
                selected = Card("EB01-025");
                me.Hand.Add(selected);
                break;
            case "OP02-089":
            case "OP02-090":
            case "OP02-091":
                for (int i = 0; i < 6; i++)
                    opp.CostArea.Add(new DonCard { State = DonState.Active });
                break;
            case "OP05-115":
                me.Hand.AddRange([Card("OP15-003"), Card("OP15-004")]);
                me.Deck.Add(Card("OP15-005"));
                break;
            case "OP06-057":
                selected = Card("EB01-026");
                me.Hand.Add(selected);
                break;
            case "OP07-078":
            case "OP10-080":
            case "OP11-081":
                me.DonDeck.Add(new DonCard { State = DonState.InDeck });
                break;
            case "OP09-107":
                selected = Card("EB01-052");
                me.Hand.Add(selected);
                break;
            case "OP14-018":
                selected = Card("EB03-004");
                me.Hand.Add(selected);
                break;
            case "OP14-082":
                selected = Card("OP06-082");
                me.Trash.Add(selected);
                break;
            case "OP14-117":
                selected = Card("OP06-083");
                me.Trash.Add(selected);
                break;
            case "OP16-101":
                selected = Card("EB01-007");
                me.Trash.Add(selected);
                break;
            case "OP16-117":
                selected = Card("OP09-088");
                me.Trash.Add(selected);
                break;
            case "ST36-002":
                me.Trash.Add(source);
                for (int i = 0; i < 3; i++) opp.LifeArea.Add(Card("OP15-003"));
                break;
        }

        await EffectRuntime.Resolve(state, 0, source,
            EffectTrigger.OnLifeRevealTrigger, new MockPromptService());

        switch (sourceNumber)
        {
            case "EB01-026":
            case "ST13-017":
            case "ST13-018":
                Assert.Same(selected, Assert.Single(me.LifeArea));
                Assert.Single(me.Hand); // 原生命牌进入手牌。
                break;
            case "EB01-035":
                Assert.Contains(source, me.Characters);
                Assert.Empty(me.CostArea);
                Assert.Single(me.DonDeck);
                break;
            case "EB01-038":
                Assert.Equal(2, me.Hand.Count);
                Assert.Empty(me.CostArea);
                Assert.Single(me.DonDeck);
                break;
            case "EB04-027":
            case "OP03-119":
            case "OP06-057":
            case "OP09-107":
            case "OP14-018":
            case "OP14-118":
                Assert.Contains(selected!, me.Characters);
                Assert.DoesNotContain(selected!, me.Hand);
                break;
            case "OP02-089":
            case "OP02-090":
            case "OP02-091":
                Assert.Equal(5, opp.CostArea.Count);
                Assert.Single(opp.DonDeck);
                break;
            case "OP05-115":
                Assert.Empty(me.Hand);
                Assert.Equal(2, me.Trash.Count);
                Assert.Single(me.LifeArea);
                Assert.Empty(me.Deck);
                break;
            case "OP07-078":
            case "OP10-080":
            case "OP11-081":
                Assert.Empty(me.DonDeck);
                Assert.Equal(1, me.ActiveDonCount);
                break;
            case "OP14-082":
            case "OP14-117":
                Assert.Contains(selected!, me.Characters);
                Assert.True(selected!.IsTapped);
                Assert.DoesNotContain(selected!, me.Trash);
                break;
            case "OP16-101":
            case "OP16-117":
                Assert.Contains(selected!, me.Hand);
                Assert.DoesNotContain(selected!, me.Trash);
                break;
            case "ST36-002":
                Assert.Contains(source, me.Characters);
                Assert.DoesNotContain(source, me.Trash);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(PlayAndMoveCards))]
    public async Task LifeTrigger_WithoutLegalCardsOrCost_LeavesZonesUnchanged(string sourceNumber)
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var opp = state.Players[1];
        var source = Card(sourceNumber);

        switch (sourceNumber)
        {
            case "EB01-035":
            case "ST36-002":
                me.Trash.Add(source);
                if (sourceNumber == "ST36-002")
                    for (int i = 0; i < 4; i++) opp.LifeArea.Add(Card("OP15-003"));
                break;
            case "EB01-038":
                me.Deck.AddRange([Card("OP15-003"), Card("OP15-004")]);
                break;
            case "OP02-089":
            case "OP02-090":
            case "OP02-091":
                for (int i = 0; i < 5; i++)
                    opp.CostArea.Add(new DonCard { State = DonState.Active });
                break;
            case "OP05-115":
                me.Hand.Add(Card("OP15-003"));
                me.Deck.Add(Card("OP15-004"));
                break;
        }

        int handBefore = me.Hand.Count;
        int deckBefore = me.Deck.Count;
        int lifeBefore = me.LifeArea.Count;
        int trashBefore = me.Trash.Count;

        await EffectRuntime.Resolve(state, 0, source,
            EffectTrigger.OnLifeRevealTrigger, new MockPromptService());

        Assert.Equal(handBefore, me.Hand.Count);
        Assert.Equal(deckBefore, me.Deck.Count);
        Assert.Equal(lifeBefore, me.LifeArea.Count);
        Assert.Equal(trashBefore, me.Trash.Count);
        Assert.Empty(me.Characters);
        Assert.Equal(sourceNumber is "OP02-089" or "OP02-090" or "OP02-091" ? 5 : 0,
            opp.CostArea.Count);
        Assert.Empty(opp.DonDeck);
    }

    [Fact]
    public async Task HandPlayTrigger_WhenFieldIsFull_UsesNormalOverflowFlow()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        for (int i = 0; i < 5; i++) me.Characters.Add(Card("EB01-005"));
        var oldCharacter = me.Characters[0];
        var selected = Card("EB01-025");
        me.Hand.Add(selected);
        var source = Card("EB04-027");
        var prompts = new MockPromptService()
            .QueueChoose(selected.Id.ToString())
            .QueueChoose(oldCharacter.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source,
            EffectTrigger.OnLifeRevealTrigger, prompts);

        Assert.Equal(5, me.Characters.Count);
        Assert.Contains(selected, me.Characters);
        Assert.Contains(oldCharacter, me.Trash);
    }

    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };
}
