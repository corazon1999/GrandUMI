using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP18EB05EffectTests
{
    private static CardInstance Card(string number, int turnPlayed = 0)
        => new() { Info = CardDatabase.Get(number)!, TurnPlayed = turnPlayed };

    private static CardInstance CustomCharacter(
        string number,
        int cost,
        int power,
        string property,
        params string[] keywords)
        => new()
        {
            Info = new CardInfo
            {
                Number = number,
                Name = number,
                Color = "绿",
                Kind = CardKind.Character,
                Property = property,
                Cost = cost,
                Power = power,
                Keywords = keywords,
            },
        };

    private static CardInstance CustomStage(string number, int cost)
        => new()
        {
            Info = new CardInfo
            {
                Number = number,
                Name = number,
                Color = "绿",
                Kind = CardKind.Stage,
                Property = "舞台卡",
                Cost = cost,
            },
        };

    [Fact]
    public async Task OP18_021_StageCardsGainCounterAndActivatedMainPlaysStageAfterDonMinusOne()
    {
        var state = TestScene.New("OP18-021").Build();
        var me = state.Players[0];
        var stage = CustomStage("TEST-STAGE", 5);
        me.Hand.Add(stage);
        var don = new DonCard { State = DonState.Active };
        me.CostArea.Add(don);
        var prompts = new MockPromptService()
            .QueueChoose(don.Id.ToString())
            .QueueChoose(stage.Id.ToString());

        Assert.Equal(3000, HandStaticCounter.Value(state, 0, stage));

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, prompts);

        Assert.Same(stage, me.StageCard);
        Assert.DoesNotContain(stage, me.Hand);
        Assert.Empty(me.CostArea);
        Assert.Contains(don, me.DonDeck);
        Assert.Contains($"OP18-021-main:{me.Leader.Id}", me.TurnOnceUsed);
        Assert.Contains(me.Leader.Id, me.OncePerTurnEffectUsedCardIds);
    }

    [Fact]
    public async Task OP18_031_RestsItselfToGuardEffectLeaveAndActivatesWaterSevenCardAndDonAtTurnEnd()
    {
        var state = TestScene.New("OP18-021").Build();
        var me = state.Players[0];
        var robin = Card("OP18-031");
        var victim = Card("OP18-031");
        me.Characters.AddRange([robin, victim]);
        state.CurrentTurnPlayer = 1;
        var guardPrompts = new MockPromptService().QueueConfirm(true);

        await EffectRuntime.Resolve(state, 0, robin, EffectTrigger.OnAllyWillLeaveField, guardPrompts,
            new Dictionary<string, object?>
            {
                ["victimId"] = victim.Id.ToString(),
                ["victimOwner"] = 0,
                ["kind"] = "bounce",
            });

        Assert.True(robin.IsTapped);
        Assert.Contains(victim.Id, state.PreventLeaveCardIds);

        state.CurrentTurnPlayer = 0;
        victim.IsTapped = true;
        var don = new DonCard { State = DonState.Rest };
        me.CostArea.Add(don);
        var turnEndPrompts = new MockPromptService()
            .QueueChoose(victim.Id.ToString())
            .QueueChoose(don.Id.ToString());

        await EffectRuntime.Resolve(state, 0, robin, EffectTrigger.OnMyTurnEnd, turnEndPrompts);

        Assert.False(victim.IsTapped);
        Assert.Equal(DonState.Active, don.State);
    }

    [Fact]
    public async Task OP18_060_TrashEntryDrawsTwoDiscardsOneAndActivatedMainAddsActiveDon()
    {
        var state = TestScene.New("OP18-060").Build();
        var me = state.Players[0];
        var oldHand = Card("OP18-031");
        var firstDraw = Card("EB05-016");
        var secondDraw = Card("OP18-078");
        me.Hand.Add(oldHand);
        me.Deck.AddRange([firstDraw, secondDraw]);
        var enterPrompts = new MockPromptService().QueueChoose(oldHand.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAllyCharEnter, enterPrompts,
            new Dictionary<string, object?> { ["owner"] = 0, ["from"] = "trash" });

        Assert.Contains(firstDraw, me.Hand);
        Assert.Contains(secondDraw, me.Hand);
        Assert.Contains(oldHand, me.Trash);
        Assert.Contains($"OP18-060-trash-enter:{me.Leader.Id}", me.TurnOnceUsed);

        me.Characters.Add(Card("OP18-119"));
        var don = new DonCard { State = DonState.InDeck };
        me.DonDeck.Add(don);
        var activePrompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(firstDraw.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, activePrompts);

        Assert.Contains(firstDraw, me.Trash);
        Assert.Contains(don, me.CostArea);
        Assert.Equal(DonState.Active, don.State);
        Assert.Contains($"OP18-060-main:{me.Leader.Id}", me.TurnOnceUsed);
        Assert.Contains(me.Leader.Id, me.OncePerTurnEffectUsedCardIds);
    }

    [Fact]
    public async Task OP18_065_DonMinusOnePlaysLowPowerCelestialDragonFromTrash()
    {
        var state = TestScene.New("OP18-060").Build();
        var me = state.Players[0];
        var source = Card("OP18-065");
        var target = CustomCharacter("TEST-TENRYU", 5, 6000, "特", "天龙人");
        me.Characters.Add(source);
        me.Trash.Add(target);
        var don = new DonCard { State = DonState.Rest };
        me.CostArea.Add(don);
        var prompts = new MockPromptService()
            .QueueChoose(don.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        Assert.Contains(target, me.Characters);
        Assert.DoesNotContain(target, me.Trash);
        Assert.Empty(me.CostArea);
        Assert.Contains(don, me.DonDeck);
    }

    [Fact]
    public async Task OP18_078_OnPlayDrawsAndAddsRestDonThenActivatedMainAssignsOneDonPerTarget()
    {
        var state = TestScene.New("OP18-021").Build();
        var me = state.Players[0];
        var source = Card("OP18-078");
        me.StageCard = source;
        var draw = Card("OP18-031");
        me.Deck.Add(draw);
        var deckDon = new DonCard { State = DonState.InDeck };
        me.DonDeck.Add(deckDon);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Contains(draw, me.Hand);
        Assert.Equal(DonState.Rest, deckDon.State);
        var first = Card("OP18-031");
        var second = Card("EB05-016");
        me.Characters.AddRange([first, second]);
        var restedDon1 = new DonCard { State = DonState.Rest };
        var restedDon2 = new DonCard { State = DonState.Rest };
        me.CostArea.AddRange([restedDon1, restedDon2]);
        var prompts = new MockPromptService()
            .QueueChoose(me.Leader.Id.ToString(), first.Id.ToString(), second.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.True(source.IsTapped);
        Assert.Equal(3, me.CostArea.Count(don => don.State == DonState.Attached));
        Assert.Equal(1, me.AttachedDonCount(me.Leader.Id));
        Assert.Equal(1, me.AttachedDonCount(first.Id));
        Assert.Equal(1, me.AttachedDonCount(second.Id));
    }

    [Fact]
    public async Task OP18_119_OnKORevivesItselfAndCanReviveKnightDuringEntryTurn()
    {
        var state = TestScene.New("OP18-060").Build();
        var me = state.Players[0];
        var source = Card("OP18-119");
        var discard = Card("OP18-031");
        me.Trash.Add(source);
        me.Hand.Add(discard);
        var koPrompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(discard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO, koPrompts);

        Assert.Contains(source, me.Characters);
        Assert.Equal(state.TurnCount, source.TurnPlayed);
        Assert.Contains(discard, me.Trash);

        var knight = CustomCharacter("TEST-KNIGHT", 6, 6000, "斩", "神之骑士团");
        me.Trash.Add(knight);
        var activePrompts = new MockPromptService().QueueChoose(knight.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, activePrompts);

        Assert.Contains(knight, me.Characters);
        Assert.Contains($"OP18-119-main:{source.Id}", me.TurnOnceUsed);
        Assert.Contains(source.Id, me.OncePerTurnEffectUsedCardIds);
    }

    [Fact]
    public async Task EB05_010_KnowledgeNonBlockerKOAddsLifeAndActivatedMainReadiesKnowledgeCharacter()
    {
        var state = TestScene.New("EB05-010").Build();
        var me = state.Players[0];
        var knockedOut = Card("OP18-031");
        me.Trash.Add(knockedOut);
        var life = Card("EB05-016");
        me.Deck.Add(life);
        var koPrompts = new MockPromptService().QueueConfirm(true);

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.OnAnyCharKOd, koPrompts,
            new Dictionary<string, object?>
            {
                ["owner"] = 0,
                ["cardId"] = knockedOut.Id.ToString(),
                ["reason"] = "battle",
            });

        Assert.Same(life, Assert.Single(me.LifeArea));
        Assert.Contains($"EB05-010-ko:{me.Leader.Id}", me.TurnOnceUsed);
        Assert.Contains(me.Leader.Id, me.OncePerTurnEffectUsedCardIds);

        var target = CustomCharacter("TEST-KNOWLEDGE", 4, 6000, "知");
        target.IsTapped = true;
        me.Characters.Add(target);
        var activePrompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain, activePrompts);

        Assert.False(target.IsTapped);
        Assert.Contains($"EB05-010-main:{me.Leader.Id}", me.TurnOnceUsed);
    }

    [Fact]
    public async Task EB05_016_OnPlayPlaysLowCostKnowledgeCharacterAndTriggerRestsOpponent()
    {
        var state = TestScene.New("EB05-010").Build();
        var me = state.Players[0];
        var source = Card("EB05-016");
        me.Characters.Add(source);
        var handTarget = Card("OP18-031");
        me.Hand.Add(handTarget);
        var onPlayPrompts = new MockPromptService().QueueChoose(handTarget.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, onPlayPrompts);

        Assert.Contains(handTarget, me.Characters);
        Assert.DoesNotContain(handTarget, me.Hand);

        var opponent = state.Players[1];
        var restTarget = Card("OP18-031");
        opponent.Characters.Add(restTarget);
        var triggerPrompts = new MockPromptService().QueueChoose(restTarget.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnLifeRevealTrigger, triggerPrompts);

        Assert.True(restTarget.IsTapped);
    }
}
