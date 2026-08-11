using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>游戏内 F 反馈入口上报的卡牌效果回归测试。</summary>
public class FFeedbackCardRegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP16_074_ForcesOpponentToReturnDon_OnEnterAndOnKO()
    {
        var enterState = TestScene.New(myLeaderNumber: "OP16-022")
            .MyActiveDon(1)
            .OppActiveDon(1)
            .Build();
        var enterMagellan = Card("OP16-074");

        await EffectRuntime.Resolve(
            enterState, 0, enterMagellan, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Single(enterState.Players[0].CostArea);
        Assert.Empty(enterState.Players[1].CostArea);

        var koState = TestScene.New()
            .MyActiveDon(4)
            .OppActiveDon(4)
            .Build();
        var koMagellan = Card("OP16-074");

        await EffectRuntime.Resolve(
            koState, 0, koMagellan, EffectTrigger.OnKO, new MockPromptService());

        Assert.Equal(4, koState.Players[0].CostArea.Count);
        Assert.Empty(koState.Players[1].CostArea);
    }

    [Fact]
    public async Task ST14_001_CostBoostOnlyAffectsOwnCharactersOnField()
    {
        var state = TestScene.New(myLeaderNumber: "ST14-001")
            .AttachDonToMyLeader(1)
            .MyCharacter("ST14-007")
            .MyHandAdd("ST14-007")
            .Build();
        var fieldCard = Assert.Single(state.Players[0].Characters);
        var handCard = Assert.Single(state.Players[0].Hand);

        await EffectRuntime.Resolve(
            state, 0, state.Players[0].Leader, EffectTrigger.OnGameStart, new MockPromptService());

        Assert.Equal(fieldCard.Info.Cost + 1, state.CurrentCostOf(0, fieldCard));
        Assert.Equal(handCard.Info.Cost, state.HandPlayCost(0, handCard));
    }

    [Fact]
    public async Task OP09_102_CloverCanSelectTriggerCardFromRobinSearch()
    {
        var state = TestScene.New(myLeaderNumber: "OP09-062")
            .MyDeckTop("OP09-109", "OP15-050", "OP15-051")
            .Build();
        var clover = Card("OP09-102");
        state.Players[0].Characters.Add(clover);
        var triggerCard = state.Players[0].Deck[0];
        var prompts = new MockPromptService().QueueChoose(triggerCard.Id.ToString());

        await EffectRuntime.Resolve(state, 0, clover, EffectTrigger.OnEnterField, prompts);

        var search = prompts.ChooseHistory[0];
        Assert.Equal("LookTopReveal", search.kind);
        Assert.Contains(triggerCard.Id.ToString(), search.choices);
        Assert.Equal("ReorderToDeckBottom", prompts.ChooseHistory[1].kind);
        Assert.Contains(triggerCard, state.Players[0].Hand);
    }

    [Fact]
    public async Task OP10_030_CannotRefreshDonTwiceInSameTurn()
    {
        var state = TestScene.New().MyCharacter("OP10-030").Build();
        var smoker = Assert.Single(state.Players[0].Characters);
        state.Players[0].CostArea.AddRange([
            new DonCard { State = DonState.Rest },
            new DonCard { State = DonState.Rest },
        ]);

        await EffectRuntime.Resolve(
            state, 0, smoker, EffectTrigger.ActivatedMain, new MockPromptService());
        await EffectRuntime.Resolve(
            state, 0, smoker, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.Equal(1, state.Players[0].CostArea.Count(don => don.State == DonState.Active));
        Assert.Equal(1, state.Players[0].CostArea.Count(don => don.State == DonState.Rest));
        Assert.Contains(0, state.NoActivateDonByCharacterEffectThisTurn);
    }

    [Fact]
    public async Task EB04_040_CanChooseKaidoLeaderForPowerBoost()
    {
        var state = TestScene.New(myLeaderNumber: "ST04-001")
            .MyActiveDon(6)
            .OppCharacter("OP15-050")
            .Build();
        var leader = state.Players[0].Leader;
        var opponent = Assert.Single(state.Players[1].Characters);
        var fireDragon = Card("EB04-040");
        var prompts = new MockPromptService()
            .QueueChoose(leader.Id.ToString())
            .QueueChoose(opponent.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, fireDragon, EffectTrigger.EventMain, prompts);

        var targetPrompt = prompts.ChooseHistory.First(prompt => prompt.kind == "OwnLeaderOrCharacter");
        Assert.Contains(leader.Id.ToString(), targetPrompt.choices);
        Assert.Equal(3000, leader.PowerModThisTurn);
        Assert.True(opponent.IsTapped);
    }

    [Fact]
    public async Task OP07_071_ReducesOpponentCharactersButNotLeader()
    {
        var state = TestScene.New(myLeaderNumber: "OP07-059")
            .OppCharacter("OP15-050")
            .Build();
        var foxy = Card("OP07-071");
        state.Players[0].Characters.Add(foxy);
        state.CurrentTurnPlayer = 1;

        await EffectRuntime.Resolve(
            state, 0, foxy, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(0, state.ContinuousPowerBonus(1, state.Players[1].Leader));
        Assert.Equal(-1000, state.ContinuousPowerBonus(1, state.Players[1].Characters[0]));
    }

    [Fact]
    public async Task OP14_027_WhenRestedByEffect_CanRestEligibleOpponentCharacter()
    {
        var state = TestScene.New()
            .MyCharacter("OP14-027")
            .OppCharacter("OP15-050")
            .Build();
        var jacks = Assert.Single(state.Players[0].Characters);
        var target = Assert.Single(state.Players[1].Characters);
        var raijinNyon = Card("OP08-036");
        var prompts = new MockPromptService()
            .QueueChoose(jacks.Id.ToString())
            .QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(
            state, 1, raijinNyon, EffectTrigger.OnLifeRevealTrigger, prompts);

        Assert.True(jacks.IsTapped);
        Assert.True(target.IsTapped);
        Assert.Contains(prompts.ChooseHistory, prompt =>
            prompt.kind == "OpponentCharacter" && prompt.choices.Contains(target.Id.ToString()));
    }

    [Fact]
    public async Task OP14_027_AfterKO_NoLongerReducesOpponentCharacters()
    {
        var state = TestScene.New()
            .MyCharacter("OP14-027")
            .OppCharacter("OP15-050")
            .Build();
        var jacks = Assert.Single(state.Players[0].Characters);
        var target = Assert.Single(state.Players[1].Characters);
        jacks.IsTapped = true;
        state.CurrentTurnPlayer = 1;

        await EffectRuntime.Resolve(
            state, 0, jacks, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(-1000, state.ContinuousPowerBonus(1, target));

        await BattleEngine.KOCardAsync(state, 0, jacks, new MockPromptService());

        Assert.Equal(0, state.ContinuousPowerBonus(1, target));
        Assert.DoesNotContain(state.ContinuousEffects, effect => effect.SourceCardId == jacks.Id.ToString());
    }
}
