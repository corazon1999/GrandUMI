using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>OP12-037「亡者游戏」主要/反击效果的目标范围回归测试。</summary>
public class OP12_037_AsuraRegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task Main_ChoosesActiveOpponentCharacterAndDonFromSamePrompt()
    {
        var state = TestScene.New().MyActiveDon(3).Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var ownCharacter = Card("OP15-003");
        var activeCharacter = Card("OP15-004");
        var restedCharacter = Card("OP15-005");
        restedCharacter.IsTapped = true;
        var unselectableCharacter = Card("OP15-006");
        unselectableCharacter.Restrictions.Add(new CardRestriction
        {
            Kind = RestrictionKind.CannotBeChosen,
            Duration = KeywordDuration.ThisTurn,
        });
        var unrestableCharacter = Card("OP15-007");
        unrestableCharacter.Restrictions.Add(new CardRestriction
        {
            Kind = RestrictionKind.CannotBeRested,
            Duration = KeywordDuration.ThisTurn,
        });
        var activeDon = new DonCard { State = DonState.Active };
        var restedDon = new DonCard { State = DonState.Rest };
        me.Characters.Add(ownCharacter);
        opponent.Characters.AddRange([
            activeCharacter,
            restedCharacter,
            unselectableCharacter,
            unrestableCharacter,
        ]);
        opponent.CostArea.AddRange([activeDon, restedDon]);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(activeCharacter.Id.ToString(), activeDon.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP12-037"), EffectTrigger.EventMain, prompts);

        Assert.Equal(3, me.CostArea.Count(don => don.State == DonState.Rest));
        Assert.True(activeCharacter.IsTapped);
        Assert.Equal(DonState.Rest, activeDon.State);
        Assert.True(restedCharacter.IsTapped);
        Assert.Equal(DonState.Rest, restedDon.State);
        Assert.False(unselectableCharacter.IsTapped);
        Assert.False(unrestableCharacter.IsTapped);
        Assert.False(ownCharacter.IsTapped);
        Assert.False(opponent.Leader.IsTapped);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OpponentCharacterOrDon", prompt.kind);
        Assert.Equal(0, prompt.min);
        Assert.Equal(2, prompt.max);
        Assert.Equal(
            [activeCharacter.Id.ToString(), activeDon.Id.ToString()],
            prompt.choices);
        Assert.NotNull(prompt.extra);
        var extraJson = System.Text.Json.JsonSerializer.Serialize(prompt.extra);
        Assert.Contains(activeCharacter.Id.ToString(), extraJson);
        Assert.Contains(activeDon.Id.ToString(), extraJson);
        Assert.DoesNotContain(restedCharacter.Id.ToString(), extraJson);
        Assert.DoesNotContain(restedDon.Id.ToString(), extraJson);
        Assert.DoesNotContain(unselectableCharacter.Id.ToString(), extraJson);
        Assert.DoesNotContain(unrestableCharacter.Id.ToString(), extraJson);
    }

    [Fact]
    public async Task Main_DecliningOptionalCostKeepsAllCardsActiveAndSkipsTargetPrompt()
    {
        var state = TestScene.New().MyActiveDon(3).Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var character = Card("OP15-003");
        var don = new DonCard { State = DonState.Active };
        opponent.Characters.Add(character);
        opponent.CostArea.Add(don);
        var prompts = new MockPromptService().QueueConfirm(false);

        await EffectRuntime.Resolve(
            state, 0, Card("OP12-037"), EffectTrigger.EventMain, prompts);

        Assert.Equal(3, me.ActiveDonCount);
        Assert.False(character.IsTapped);
        Assert.Equal(DonState.Active, don.State);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public async Task Main_SelectingOnlyCharacterDoesNotAutomaticallyRestOpponentDon()
    {
        var state = TestScene.New().MyActiveDon(3).Build();
        var opponent = state.Players[1];
        var character = Card("OP15-003");
        var don = new DonCard { State = DonState.Active };
        opponent.Characters.Add(character);
        opponent.CostArea.Add(don);
        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(character.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, Card("OP12-037"), EffectTrigger.EventMain, prompts);

        Assert.True(character.IsTapped);
        Assert.Equal(DonState.Active, don.State);
    }

    [Fact]
    public async Task CounterDuringCharacterBattle_BoostsLeaderOnly()
    {
        _ = TestScene.New().Build();
        string deck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        var engine = new GameEngine("op12-037-character-target", ("s0", "p0", deck), ("s1", "p1", deck), 0, 19);
        var state = engine.State;
        var defender = state.Players[1];
        var defendedCharacter = Card("OP15-050");

        defender.Characters.Clear();
        defender.Characters.Add(defendedCharacter);
        defender.Hand.Clear();
        defender.Hand.Add(Card("OP12-037"));
        defender.CostArea.Clear();
        defender.CostArea.Add(new DonCard { State = DonState.Active });
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;

        BattleEngine.StartAttack(
            state,
            state.Players[0].Leader.Id,
            targetIsLeader: false,
            targetId: defendedCharacter.Id);
        await BattleEngine.TriggerAttackDeclareAsync(state, new MockPromptService());
        BattleEngine.PassBlock(state);

        Assert.True(engine.HandleAction(1, "PlayCounter", System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            handIndex = 0,
            useCounterIcon = false,
        })));
        await engine.WaitSettledAsync();

        Assert.Equal(3000, defender.Leader.PowerModThisBattle);
        Assert.Equal(0, defendedCharacter.PowerModThisBattle);
        Assert.Contains(defender.Trash, card => card.Info.Number == "OP12-037");
    }
}
