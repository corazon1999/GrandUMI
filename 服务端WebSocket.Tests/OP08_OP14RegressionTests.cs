using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>OP08-036 与 OP14-031 的卡效回归测试。</summary>
public class OP08_OP14RegressionTests
{
    [Fact]
    public async Task OP08_117_LifeTrigger_SwapsTopLifeWithSelectedHandCard()
    {
        var state = TestScene.New()
            .MyHandAdd("OP15-050")
            .Build();
        var me = state.Players[0];
        var handCard = Assert.Single(me.Hand);
        var lifeCard = new CardInstance { Info = CardDatabase.Get("OP15-051")! };
        me.LifeArea.Add(lifeCard);
        var source = new CardInstance { Info = CardDatabase.Get("OP08-117")! };
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(handCard.Id.ToString());

        await EffectRuntime.Resolve(
            state,
            0,
            source,
            EffectTrigger.OnLifeRevealTrigger,
            prompts);

        Assert.Contains(lifeCard, me.Hand);
        Assert.DoesNotContain(handCard, me.Hand);
        Assert.Same(handCard, Assert.Single(me.LifeArea));
        Assert.False(handCard.IsLifeFaceUp);
    }

    [Fact]
    public async Task OP08_050_WhenOnlyOneCardCanBeDrawn_ReturnsThatOneCard()
    {
        var state = TestScene.New().Build();
        var source = new CardInstance { Info = CardDatabase.Get("OP08-050")! };
        var onlyCard = new CardInstance { Info = CardDatabase.Get("OP15-050")! };
        state.Players[0].Characters.Add(source);
        state.Players[0].Deck.Add(onlyCard);
        var prompts = new MockPromptService()
            .QueueChoose(onlyCard.Id.ToString())
            .QueueOption(0);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var choice = Assert.Single(prompts.ChooseHistory);
        Assert.Equal(1, choice.min);
        Assert.Equal(1, choice.max);
        Assert.Empty(state.Players[0].Hand);
        Assert.Equal(onlyCard, Assert.Single(state.Players[0].Deck));
    }

    [Fact]
    public async Task OP08_052_OnEnter_PlaysEligibleTopCharacterDirectlyFromDeck()
    {
        var state = TestScene.New()
            .MyDeckTop("OP03-012", "OP15-050")
            .Build();
        var me = state.Players[0];
        var source = new CardInstance { Info = CardDatabase.Get("OP08-052")! };
        var eligibleTop = me.Deck[0];
        me.Characters.Add(source);
        var prompts = new MockPromptService().QueueChoose(eligibleTop.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("LookTopReveal", prompt.kind);
        Assert.Contains(eligibleTop.Id.ToString(), prompt.choices);
        Assert.DoesNotContain(eligibleTop, me.Deck);
        Assert.DoesNotContain(eligibleTop, me.Hand);
        Assert.Contains(eligibleTop, me.Characters);
        Assert.Equal("OP15-050", Assert.Single(me.Deck).Info.Number);
    }

    [Fact]
    public async Task OP14_031_OnEnter_RestsBothSelectedCharacters()
    {
        var state = TestScene.New()
            .OppCharacter("OP15-050")
            .OppCharacter("OP15-051")
            .Build();
        var source = new CardInstance { Info = CardDatabase.Get("OP14-031")! };
        state.Players[0].Characters.Add(source);
        var targets = state.Players[1].Characters.ToList();
        var prompts = new MockPromptService()
            .QueueChoose(targets[0].Id.ToString(), targets[1].Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.All(targets, target => Assert.True(target.IsTapped));
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal(2, prompt.max);
    }

    [Fact]
    public async Task OP14_027_OpponentTurn_ReducesOnlyOpponentCharactersPower()
    {
        var state = TestScene.New()
            .MyCharacter("OP15-050")
            .OppCharacter("OP15-051")
            .Build();
        var source = new CardInstance { Info = CardDatabase.Get("OP14-027")!, IsTapped = true };
        state.Players[0].Characters.Add(source);
        state.CurrentTurnPlayer = 1;

        await EffectRuntime.Resolve(
            state,
            0,
            source,
            EffectTrigger.OnEnterField,
            new MockPromptService());

        Assert.Equal(0, state.ContinuousPowerBonus(0, state.Players[0].Leader));
        Assert.Equal(0, state.ContinuousPowerBonus(1, state.Players[1].Leader));
        Assert.Equal(0, state.ContinuousPowerBonus(0, state.Players[0].Characters[0]));
        Assert.Equal(0, state.ContinuousPowerBonus(0, source));
        Assert.Equal(-1000, state.ContinuousPowerBonus(1, state.Players[1].Characters[0]));
    }

    [Fact]
    public async Task OP08_036_LifeTrigger_RestsSelectedOpponentCharacter()
    {
        var state = TestScene.New()
            .OppCharacter("OP15-050")
            .OppCharacter("OP15-051")
            .Build();
        var source = new CardInstance { Info = CardDatabase.Get("OP08-036")! };
        var untouched = state.Players[1].Characters[0];
        var selected = state.Players[1].Characters[1];
        var prompts = new MockPromptService()
            .QueueChoose(selected.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnLifeRevealTrigger, prompts);

        Assert.False(untouched.IsTapped);
        Assert.True(selected.IsTapped);
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OpponentCharacter", prompt.kind);
        Assert.Equal(1, prompt.max);
    }

    [Fact]
    public async Task OP08_036_EventMain_DoesNotAffectCharacterRaisedAboveSevenCost()
    {
        var state = TestScene.New()
            .OppCharacter("OP15-003")
            .Build();
        var source = new CardInstance { Info = CardDatabase.Get("OP08-036")! };
        var target = Assert.Single(state.Players[1].Characters);
        target.IsTapped = true;
        target.CostModThisTurn = 12;
        Assert.Equal(17, state.CurrentCostOf(1, target));

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventMain, new MockPromptService());

        Assert.False(target.CannotActivateNextReset);
    }

    [Fact]
    public async Task OP08_036_EventMain_AffectsCharacterReducedToSevenCost()
    {
        var state = TestScene.New()
            .OppCharacter("OP15-008")
            .Build();
        var source = new CardInstance { Info = CardDatabase.Get("OP08-036")! };
        var target = Assert.Single(state.Players[1].Characters);
        target.IsTapped = true;
        target.CostModThisTurn = -1;
        Assert.Equal(7, state.CurrentCostOf(1, target));

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventMain, new MockPromptService());

        Assert.True(target.CannotActivateNextReset);
    }

    [Theory]
    [InlineData("OP14-110")]
    [InlineData("OP14-111")]
    [InlineData("OP14-100")]
    public async Task OP14_110_111_LifeTrigger_PlaysSelectedThrillerBarkCharacterRested(string sourceNumber)
    {
        var state = TestScene.New().Build();
        var source = new CardInstance { Info = CardDatabase.Get(sourceNumber)! };
        var selected = new CardInstance { Info = CardDatabase.Get("OP14-102")! };
        var invalid = new CardInstance { Info = CardDatabase.Get("OP15-050")! };
        state.Players[0].Trash.AddRange([source, selected, invalid]);
        var prompts = new MockPromptService().QueueChoose(selected.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnLifeRevealTrigger, prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("PlayCharFromTrash", prompt.kind);
        Assert.Contains(selected.Id.ToString(), prompt.choices);
        Assert.DoesNotContain(invalid.Id.ToString(), prompt.choices);
        Assert.DoesNotContain(selected, state.Players[0].Trash);
        Assert.Contains(selected, state.Players[0].Characters);
        Assert.True(selected.IsTapped);
    }

    [Fact]
    public async Task OP08_105_ReceivesLifeLeaveWatcherFromScriptedEffect()
    {
        var state = TestScene.New()
            .MyCharacter("OP08-105")
            .MyDeckTop("OP15-050", "OP15-051")
            .Build();
        var bonney = state.Players[0].Characters.Single();
        state.Players[0].CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = bonney.Id });
        state.Players[0].Hand.Add(new CardInstance { Info = CardDatabase.Get("OP15-052")! });
        state.Players[1].LifeArea.Add(new CardInstance { Info = CardDatabase.Get("OP15-053")! });
        var hawkins = new CardInstance { Info = CardDatabase.Get("OP10-109")! };
        var discard = state.Players[0].Hand.Single();
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, hawkins, EffectTrigger.OnKO, prompts);

        Assert.Empty(state.Players[1].LifeArea);
        Assert.Empty(state.Players[0].Deck);
        Assert.Equal(2, state.Players[0].Hand.Count);
        Assert.DoesNotContain(state.Players[0].Hand, c => c.Id == discard.Id);
        Assert.Contains(state.Players[0].Trash, c => c.Id == discard.Id);
    }

    [Fact]
    public async Task OP14_113_SearchesAmazonLilyButNotKujaPirates()
    {
        var state = TestScene.New().Build();
        var source = new CardInstance { Info = CardDatabase.Get("OP14-113")! };
        var amazonLily = new CardInstance { Info = CardDatabase.Get("OP14-107")! };
        var kujaPirates = new CardInstance { Info = CardDatabase.Get("OP14-114")! };
        var discard = new CardInstance { Info = CardDatabase.Get("OP15-050")! };
        state.Players[0].Characters.Add(source);
        state.Players[0].Deck.AddRange([
            amazonLily,
            kujaPirates,
            new CardInstance { Info = CardDatabase.Get("OP15-050")! },
            new CardInstance { Info = CardDatabase.Get("OP15-051")! },
            new CardInstance { Info = CardDatabase.Get("OP15-052")! },
        ]);
        state.Players[0].Hand.Add(discard);
        var prompts = new MockPromptService()
            .QueueChoose(amazonLily.Id.ToString())
            .QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var search = prompts.ChooseHistory[0];
        Assert.Equal("LookTopReveal", search.kind);
        Assert.Contains(amazonLily.Id.ToString(), search.choices);
        Assert.DoesNotContain(kujaPirates.Id.ToString(), search.choices);
        Assert.Contains(amazonLily, state.Players[0].Hand);
        Assert.Contains(discard, state.Players[0].Trash);
    }
}
