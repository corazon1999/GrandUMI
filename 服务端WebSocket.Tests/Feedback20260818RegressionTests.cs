using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-08-18 玩家集中反馈回归测试。</summary>
public class Feedback20260818RegressionTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP08_052_AcceptsFormerWhitebeardPiratesByContainsMatch()
    {
        var state = TestScene.New().MyDeckTop("OP01-023").Build();
        var target = Assert.Single(state.Players[0].Deck);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP08-052"), EffectTrigger.OnEnterField, prompts);

        Assert.Contains(target, state.Players[0].Characters);
        Assert.Empty(state.Players[0].Deck);
    }

    [Fact]
    public async Task OP08_040_RevealCostAcceptsFormerWhitebeardPiratesByContainsMatch()
    {
        var state = TestScene.New("OP08-002")
            .MyHandAdd("OP01-023")
            .MyHandAdd("OP01-033")
            .OppCharacter("OP15-004")
            .Build();
        var revealed = state.Players[0].Hand.ToArray();
        var target = Assert.Single(state.Players[1].Characters);
        var prompts = new MockPromptService()
            .QueueChoose(revealed.Select(card => card.Id.ToString()).ToArray())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP08-040"), EffectTrigger.OnEnterField, prompts);

        var revealPrompt = Assert.Single(prompts.ChooseHistory.Where(item => item.kind == "RevealOwnHand"));
        Assert.All(revealed, card => Assert.Contains(card.Id.ToString(), revealPrompt.choices));
        Assert.Contains(target, state.Players[1].Hand);
    }

    [Fact]
    public async Task OP14_038_RestsTwoChosenOwnCardsInsteadOfAutomaticallyRestingDon()
    {
        var state = TestScene.New().MyCharacter("OP15-003").MyActiveDon(2).Build();
        var me = state.Players[0];
        var character = Assert.Single(me.Characters);
        var prompts = new MockPromptService()
            .QueueChoose(me.Leader.Id.ToString(), character.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP14-038"), EffectTrigger.EventMain, prompts);

        Assert.True(me.Leader.IsTapped);
        Assert.True(character.IsTapped);
        Assert.Equal(2, me.ActiveDonCount);
        var costPrompt = Assert.Single(prompts.ChooseHistory.Where(item => item.kind == "RestOwnCardsOrDon"));
        Assert.Equal(2, costPrompt.min);
        Assert.Equal(2, costPrompt.max);
    }

    [Fact]
    public void ST22_003_HasDoubleAttackKeyword()
        => Assert.Contains("双重攻击", CardDatabase.Get("ST22-003")!.Abilities);

    [Fact]
    public async Task EB03_014_AttachesTwoRestedDonToDualPropertyST12Leader()
    {
        var state = TestScene.New("ST12-001").MyCharacter("EB03-014").Build();
        var me = state.Players[0];
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        me.CostArea.Add(new DonCard { State = DonState.Rest });
        var kuina = Assert.Single(me.Characters);

        await EffectRuntime.Resolve(state, 0, kuina, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.True(kuina.IsTapped);
        Assert.Equal(2, me.AttachedDonCount(me.Leader.Id));
        Assert.Empty(me.DonDeck);
    }

    [Fact]
    public async Task OP15_092_OriginalPowerBecomesNineThousandAndIsNotASevenThousandTarget()
    {
        var state = TestScene.New("ST12-001").OppCharacter("OP15-092").Build();
        var luffy = Assert.Single(state.Players[1].Characters);
        for (int i = 0; i < 10; i++) state.Players[1].Trash.Add(Card("OP15-003"));

        await EffectRuntime.Resolve(state, 1, luffy, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(9000, state.OriginalPowerOf(1, luffy));

        var prompts = new MockPromptService().QueueChoose(luffy.Id.ToString());
        await EffectRuntime.Resolve(state, 0, Card("OP14-108"), EffectTrigger.OnEnterField, prompts);

        Assert.Contains(luffy, state.Players[1].Characters);
        Assert.DoesNotContain(luffy, state.Players[1].Trash);
    }

    [Fact]
    public async Task OP15_007_PlaysOnlyCharacterWithOriginalCostAtMostFiveFromHand()
    {
        var state = TestScene.New().MyHandAdd("OP15-003").MyHandAdd("OP13-118").Build();
        var eligible = state.Players[0].Hand.Single(card => card.Info.Number == "OP15-003");
        var ineligible = state.Players[0].Hand.Single(card => card.Info.Number == "OP13-118");
        var prompts = new MockPromptService().QueueChoose(eligible.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP15-007"), EffectTrigger.OnEnterField, prompts);

        var choose = Assert.Single(prompts.ChooseHistory);
        Assert.Contains(eligible.Id.ToString(), choose.choices);
        Assert.DoesNotContain(ineligible.Id.ToString(), choose.choices);
        Assert.Contains(eligible, state.Players[0].Characters);
        Assert.Contains(ineligible, state.Players[0].Hand);
    }

    [Fact]
    public async Task OP16_001_GrantsRushToOP16_017ByContainedWhitebeardKeyword()
    {
        var state = TestScene.New("OP16-001").MyCharacter("OP16-017").Build();
        var target = Assert.Single(state.Players[0].Characters);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader,
            EffectTrigger.ActivatedMain, prompts);

        Assert.True(ActionValidator.HasKeyword(state, target, "速攻"));
    }

    [Fact]
    public async Task OP15_025_AttachesTwoOpponentDonRegardlessOfTheirState()
    {
        var state = TestScene.New().OppCharacter("OP15-003").Build();
        var opponent = state.Players[1];
        opponent.CostArea.Add(new DonCard { State = DonState.Rest });
        opponent.CostArea.Add(new DonCard { State = DonState.Rest });
        var target = Assert.Single(opponent.Characters);
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP15-025"), EffectTrigger.OnEnterField, prompts);

        Assert.Equal(2, opponent.AttachedDonCount(target.Id));
    }

    [Fact]
    public async Task OP12_081_DoesNotTriggerForCharacterPlayedFromLife()
    {
        var state = TestScene.New().MyCharacter("OP12-081").OppCharacter("OP16-013").Build();
        var koala = Assert.Single(state.Players[0].Characters);
        var entered = Assert.Single(state.Players[1].Characters);
        state.Players[1].LifeArea.Add(Card("OP15-003"));
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, koala, EffectTrigger.OnAllyCharEnter, prompts,
            new Dictionary<string, object?>
            {
                ["owner"] = 1,
                ["cardId"] = entered.Id.ToString(),
                ["from"] = "life",
                ["effectSourceKind"] = CardKind.Character.ToString(),
            });

        Assert.Single(state.Players[1].LifeArea);
        Assert.Empty(prompts.ConfirmHistory);
    }

    [Fact]
    public async Task OP11_022_CanPayWithoutEligibleHandCardAndKeepsLifeFaceUp()
    {
        var state = TestScene.New("OP11-022").MyActiveDon(1).Build();
        var me = state.Players[0];
        me.LifeArea.Add(Card("OP15-003"));

        await EffectRuntime.Resolve(state, 0, me.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueConfirm(true));

        Assert.Equal(DonState.Rest, Assert.Single(me.CostArea).State);
        Assert.True(Assert.Single(me.LifeArea).IsLifeFaceUp);
        Assert.NotEmpty(me.TurnOnceUsed);
    }

    [Fact]
    public async Task OP15_071_SetsEveryOhmOriginalPowerToSixThousandDuringOpponentTurn()
    {
        var state = TestScene.New().MyCharacter("OP15-071").MyCharacter("OP15-061").Build();
        var pauly = state.Players[0].Characters.Single(card => card.Info.Number == "OP15-071");
        var ohm = state.Players[0].Characters.Single(card => card.Info.Number == "OP15-061");
        state.CurrentTurnPlayer = 1;

        await EffectRuntime.Resolve(state, 0, pauly, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(6000, state.OriginalPowerOf(0, ohm));
        Assert.Equal(6000, state.CurrentPowerOf(0, ohm));
    }

    [Fact]
    public async Task OP13_118_BlocksOriginalCostFiveOrHigherCharactersForTheTurn()
    {
        var state = TestScene.New("ST12-001").MyHandAdd("OP16-017").MyHandAdd("OP13-118").Build();
        var me = state.Players[0];
        for (int i = 0; i < 4; i++) me.CostArea.Add(new DonCard { State = DonState.Rest });

        await EffectRuntime.Resolve(state, 0, Card("OP13-118"), EffectTrigger.OnEnterField,
            new MockPromptService().QueueOption(4));

        Assert.Equal(4, me.ActiveDonCount);
        int highIndex = me.Hand.FindIndex(card => card.Info.Number == "OP13-118");
        Assert.False(ActionValidator.CanPlayCard(state, 0, highIndex).Ok);
        var high = me.Hand[highIndex];
        await AtomicOps.PlayFromHandFree(state, 0, high);
        Assert.Contains(high, me.Hand);

        var low = me.Hand.Single(card => card.Info.Number == "OP16-017");
        await AtomicOps.PlayFromHandFree(state, 0, low);
        Assert.Contains(low, me.Characters);
    }

    [Fact]
    public async Task OP15_023_SelectsActiveDonFromTargetHoldersCostArea()
    {
        var state = TestScene.New().MyCharacter("OP15-023").OppCharacter("OP15-003").Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var ownActiveDon = new DonCard { State = DonState.Active };
        me.CostArea.Add(ownActiveDon);
        opponent.CostArea.Add(new DonCard { State = DonState.Rest });
        var source = Assert.Single(me.Characters);
        var opponentCharacter = Assert.Single(opponent.Characters);
        var prompts = new MockPromptService()
            .QueueChoose(opponentCharacter.Id.ToString())
            .QueueChoose(me.Leader.Id.ToString())
            .QueueChoose(ownActiveDon.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.Equal(1, opponent.AttachedDonCount(opponentCharacter.Id));
        Assert.Equal(1, me.AttachedDonCount(me.Leader.Id));
    }

    [Fact]
    public async Task OP14_110_LifeTriggerCannotSelectItselfFromTemporaryTrashPosition()
    {
        var state = TestScene.New().Build();
        var source = Card("OP14-110");
        state.Players[0].Trash.Add(source);
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnLifeRevealTrigger, prompts);

        Assert.Contains(source, state.Players[0].Trash);
        Assert.DoesNotContain(source, state.Players[0].Characters);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task OP15_114_UsesOneSimultaneousKoProcessSoOP17_095ProtectsAllVictims()
    {
        var state = TestScene.New().Build();
        var attacker = state.Players[0];
        var defender = state.Players[1];
        attacker.LifeArea.Add(Card("OP15-003"));
        var guard = Card("OP17-095");
        var first = Card("ST30-006");
        var second = Card("ST30-007");
        defender.Characters.AddRange([guard, first, second]);
        var trash = new[] { Card("ST30-002"), Card("ST30-003"), Card("ST30-004") };
        defender.Trash.AddRange(trash);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueConfirm(true)
            .QueueChoose(trash.Select(card => card.Id.ToString()).ToArray());

        await EffectRuntime.Resolve(state, 0, Card("OP15-114"), EffectTrigger.OnEnterField, prompts);

        Assert.Contains(first, defender.Characters);
        Assert.Contains(second, defender.Characters);
        Assert.Empty(defender.Trash);
        Assert.Equal(trash, defender.Deck);
        Assert.Equal(2, prompts.ConfirmHistory.Count);
    }
}
