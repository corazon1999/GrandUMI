using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

public class ST14EffectTests
{
    [Fact]
    public async Task ST14_017_SunnyGo_DynamicallyBoostsOwnMatchingCharactersOnField()
    {
        var state = TestScene.New(myLeaderNumber: "ST14-001")
            .MyCharacter("ST14-007")
            .MyHandAdd("ST14-007")
            .OppCharacter("ST14-007")
            .MyDeckTop("OP15-050")
            .Build();

        var ownFieldCharacter = state.Players[0].Characters[0];
        var ownHandCharacter = state.Players[0].Hand[0];
        var opponentFieldCharacter = state.Players[1].Characters[0];
        var sunny = new CardInstance { Info = CardDatabase.Get("ST14-017")! };
        state.Players[0].StageCard = sunny;

        await EffectRuntime.Resolve(
            state, 0, sunny, EffectTrigger.OnEnterField, new MockPromptService());

        var laterCharacter = new CardInstance { Info = CardDatabase.Get("ST14-007")! };
        state.Players[0].Characters.Add(laterCharacter);

        Assert.Equal(ownFieldCharacter.Info.Cost + 1, state.CurrentCostOf(0, ownFieldCharacter));
        Assert.Equal(laterCharacter.Info.Cost + 1, state.CurrentCostOf(0, laterCharacter));
        Assert.Equal(ownHandCharacter.Info.Cost, state.HandPlayCost(0, ownHandCharacter));
        Assert.Equal(opponentFieldCharacter.Info.Cost, state.CurrentCostOf(1, opponentFieldCharacter));

        var fullCheckpoint = DeterministicReplayCheckpointProvider.BuildFullState(state);
        var ownCharacters = fullCheckpoint.GetProperty("players")[0].GetProperty("characters");
        Assert.Empty(ownCharacters[0].GetProperty("fieldSnapshotSourceIds").EnumerateArray());
        Assert.Empty(ownCharacters[1].GetProperty("fieldSnapshotSourceIds").EnumerateArray());

        var recoveryState = System.Text.Json.JsonSerializer.SerializeToElement(
            PrivateStateSnapshotBuilder.Build(state));
        var recoveryCharacters = recoveryState.GetProperty("players")[0].GetProperty("characters");
        Assert.Empty(recoveryCharacters[0].GetProperty("fieldSnapshotSourceIds").EnumerateArray());
        Assert.Empty(recoveryCharacters[1].GetProperty("fieldSnapshotSourceIds").EnumerateArray());
    }

    [Fact]
    public async Task ST14_017_SunnyGo_DynamicAuraStopsOnNullificationOrStageDeparture()
    {
        var state = TestScene.New(myLeaderNumber: "ST14-001")
            .MyCharacter("ST14-007")
            .MyDeckTop("OP15-050")
            .Build();
        var me = state.Players[0];
        var capturedCharacter = me.Characters[0];
        var sunny = new CardInstance { Info = CardDatabase.Get("ST14-017")! };
        me.StageCard = sunny;

        await EffectRuntime.Resolve(
            state, 0, sunny, EffectTrigger.OnEnterField, new MockPromptService());

        sunny.IsEffectsNullified = true;
        Assert.Equal(capturedCharacter.Info.Cost, state.CurrentCostOf(0, capturedCharacter));
        sunny.IsEffectsNullified = false;
        Assert.Equal(capturedCharacter.Info.Cost + 1, state.CurrentCostOf(0, capturedCharacter));

        Assert.True(me.Characters.Remove(capturedCharacter));
        me.Characters.Add(capturedCharacter);
        Assert.Equal(capturedCharacter.Info.Cost + 1, state.CurrentCostOf(0, capturedCharacter));

        AtomicOps.BounceToHand(state, 0, sunny);
        Assert.Equal(capturedCharacter.Info.Cost, state.CurrentCostOf(0, capturedCharacter));

        Assert.True(me.Hand.Remove(sunny));
        me.StageCard = sunny;
        await EffectRuntime.Resolve(
            state, 0, sunny, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(capturedCharacter.Info.Cost + 1, state.CurrentCostOf(0, capturedCharacter));
    }

    [Fact]
    public async Task ST14_017_SunnyGo_RepeatedRegistrationIsIdempotentAndRemainsDynamic()
    {
        var state = TestScene.New(myLeaderNumber: "ST14-001")
            .MyCharacter("ST14-007")
            .MyDeckTop("OP15-050", "OP15-051")
            .Build();
        var sunny = new CardInstance { Info = CardDatabase.Get("ST14-017")! };
        state.Players[0].StageCard = sunny;

        await EffectRuntime.Resolve(
            state, 0, sunny, EffectTrigger.OnEnterField, new MockPromptService());
        var laterCharacter = new CardInstance { Info = CardDatabase.Get("ST14-007")! };
        state.Players[0].Characters.Add(laterCharacter);

        await EffectRuntime.Resolve(
            state, 0, sunny, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(laterCharacter.Info.Cost + 1, state.CurrentCostOf(0, laterCharacter));
        Assert.Single(
            state.ContinuousEffects,
            effect => effect.SourceCardId == sunny.Id.ToString() && effect.CostDelta == 1);
    }

    [Fact]
    public async Task ST14_017_SunnyGo_TwoStagesStackAndLeaveIndependently()
    {
        var state = TestScene.New(myLeaderNumber: "ST14-001")
            .MyCharacter("ST14-007")
            .MyDeckTop("OP15-050", "OP15-051")
            .Build();
        var me = state.Players[0];
        var capturedCharacter = me.Characters[0];
        var firstSunny = new CardInstance { Info = CardDatabase.Get("ST14-017")! };
        var secondSunny = new CardInstance { Info = CardDatabase.Get("ST14-017")! };
        me.StageCard = firstSunny;
        me.ExtraStageCard = secondSunny;

        await EffectRuntime.Resolve(
            state, 0, firstSunny, EffectTrigger.OnEnterField, new MockPromptService());
        await EffectRuntime.Resolve(
            state, 0, secondSunny, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(capturedCharacter.Info.Cost + 2, state.CurrentCostOf(0, capturedCharacter));
        Assert.Empty(capturedCharacter.FieldSnapshotSourceIds);

        me.StageCard = null;
        Assert.Equal(capturedCharacter.Info.Cost + 1, state.CurrentCostOf(0, capturedCharacter));
        Assert.DoesNotContain(state.ContinuousEffects, effect =>
            effect.SourceCardId.StartsWith(firstSunny.Id.ToString(), StringComparison.Ordinal));
        Assert.Contains(state.ContinuousEffects, effect =>
            effect.SourceCardId.StartsWith(secondSunny.Id.ToString(), StringComparison.Ordinal));

        me.ExtraStageCard = null;
        Assert.Equal(capturedCharacter.Info.Cost, state.CurrentCostOf(0, capturedCharacter));
        Assert.Empty(capturedCharacter.FieldSnapshotSourceIds);
    }

    [Theory]
    [InlineData(EffectTrigger.OnEnterField)]
    [InlineData(EffectTrigger.OnAttackDeclare)]
    public async Task ST14_007_Robin_ActivatesWhenCostBoostedToEight(EffectTrigger trigger)
    {
        var state = TestScene.New(myLeaderNumber: "ST14-001")
            .AttachDonToMyLeader(1)
            .MyCharacter("ST14-007")
            .OppCharacter("OP15-050")
            .MyDeckTop("OP15-050")
            .Build();

        var robin = state.Players[0].Characters[0];
        var target = state.Players[1].Characters[0];
        var sunny = new CardInstance { Info = CardDatabase.Get("ST14-017")! };
        state.Players[0].StageCard = sunny;

        await EffectRuntime.Resolve(
            state, 0, state.Players[0].Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(
            state, 0, sunny, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(8, state.CurrentCostOf(0, robin));

        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());
        await EffectRuntime.Resolve(state, 0, robin, trigger, prompts);

        Assert.Contains(prompts.ChooseHistory, prompt => prompt.kind == "OpponentCharacter");
        Assert.Equal(-5, target.CostModThisTurn);
    }

    [Fact]
    public async Task ST14_001_Luffy_GainsPowerFromCharacterWithBoostedCost()
    {
        var state = TestScene.New(myLeaderNumber: "ST14-001")
            .AttachDonToMyLeader(1)
            .MyCharacter("ST14-007")
            .Build();

        var leader = state.Players[0].Leader;
        var robin = state.Players[0].Characters[0];
        robin.CostModThisTurn = 1;

        await EffectRuntime.Resolve(
            state, 0, leader, EffectTrigger.OnGameStart, new MockPromptService());

        Assert.Equal(8, state.CurrentCostOf(0, robin));
        Assert.Equal(
            leader.CurrentPower(state.Players[0].AttachedDonCount(leader.Id), true) + 1000,
            state.CurrentPowerOf(0, leader));
    }
}
