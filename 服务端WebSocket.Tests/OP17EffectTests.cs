using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class OP17EffectTests
{
    private static CardInstance Card(string number, int turnPlayed = 0)
        => new() { Info = CardDatabase.Get(number)!, TurnPlayed = turnPlayed };

    [Fact]
    public void OP17_063_And_118_ExposeDynamicHandCounters()
    {
        var auraState = TestScene.New("OP17-039").Build();
        var ged = Card("OP17-063");
        var noCounter = Card("OP17-044");
        auraState.Players[0].Characters.Add(ged);
        auraState.Players[0].Hand.Add(noCounter);

        Assert.Equal(1000, HandStaticCounter.Value(auraState, 0, noCounter));

        var rocksState = TestScene.New("OP17-039").Build();
        var rocks = Card("OP17-118");
        rocksState.Players[0].Hand.Add(rocks);
        rocksState.Players[0].Characters.Add(Card("OP17-044"));
        Assert.Equal(2000, HandStaticCounter.Value(rocksState, 0, rocks));

        rocksState.Players[0].Characters.Add(Card("OP17-085"));
        Assert.Equal(0, HandStaticCounter.Value(rocksState, 0, rocks));
    }

    [Fact]
    public void OP17_005_HandCostDropsByFour_WhenOpponentHas10000PowerCharacter()
    {
        var state = TestScene.New("OP17-001").Build();
        var whitebeard = Card("OP17-005");
        state.Players[0].Hand.Add(whitebeard);
        state.Players[1].Characters.Add(Card("OP17-005"));

        Assert.Equal(6, state.HandPlayCost(0, whitebeard));
    }

    [Fact]
    public async Task OP17_005_OriginalPowerOverride_ExpiresAtOpponentEndPhase()
    {
        var state = TestScene.New("OP17-001").Build();
        var whitebeard = Card("OP17-005");
        state.Players[0].Characters.Add(whitebeard);

        await EffectRuntime.Resolve(state, 0, whitebeard, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(8000, state.CurrentPowerOf(0, state.Players[0].Leader));

        state.CurrentTurnPlayer = 0;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(8000, state.CurrentPowerOf(0, state.Players[0].Leader));

        state.CurrentTurnPlayer = 1;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(5000, state.CurrentPowerOf(0, state.Players[0].Leader));
    }

    [Fact]
    public void OP17_044_RestedJohnCaptain_ForcesAllAttackTargetsToJohn()
    {
        var state = TestScene.New("OP17-039").Build();
        state.CurrentTurnPlayer = 1;
        state.TurnCount = 2;
        var john = Card("OP17-044");
        var other = Card("OP17-040");
        john.IsTapped = true;
        other.IsTapped = true;
        state.Players[0].Characters.Add(john);
        state.Players[0].Characters.Add(other);
        var attacker = state.Players[1].Leader;

        Assert.False(ActionValidator.CanAttack(state, 1, attacker.Id, true, null).Ok);
        Assert.False(ActionValidator.CanAttack(state, 1, attacker.Id, false, other.Id).Ok);
        Assert.True(ActionValidator.CanAttack(state, 1, attacker.Id, false, john.Id).Ok);
    }

    [Fact]
    public async Task OP17_079_GrantsBlockerToCharactersWhoseCurrentCostIsAtLeast12()
    {
        var state = TestScene.New("OP17-079").Build();
        var dorry = Card("OP17-085");
        state.Players[0].Characters.Add(dorry);

        await EffectRuntime.Resolve(state, 0, state.Players[0].Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(state, 0, dorry, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.True(state.CurrentCostOf(0, dorry) >= 12);
        Assert.True(ActionValidator.HasKeyword(state, dorry, "阻挡者"));
    }

    [Fact]
    public async Task OP17_107_LifeTrigger_PlaysItselfFromTrash()
    {
        var state = TestScene.New().Build();
        var daifuku = Card("OP17-107");
        state.Players[0].Trash.Add(daifuku);

        await EffectRuntime.Resolve(state, 0, daifuku, EffectTrigger.OnLifeRevealTrigger, new MockPromptService());

        Assert.Contains(daifuku, state.Players[0].Characters);
        Assert.DoesNotContain(daifuku, state.Players[0].Trash);
    }

    [Fact]
    public async Task OP17_040_LeaderBattleWatcher_DiscardsOneAndAdds3000ForBattle()
    {
        var state = TestScene.New("OP17-039").Build();
        var watcher = Card("OP17-040");
        state.Players[0].Characters.Add(watcher);
        state.Players[0].Hand.Add(Card("OP17-044"));
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            AttackerCardId = state.Players[0].Leader.Id,
            DefenderPlayerIndex = 1,
            TargetIsLeader = true,
        };

        await EffectRuntime.Resolve(state, 0, watcher, EffectTrigger.OnLeaderBattle, new MockPromptService());

        Assert.Equal(3000, state.Players[0].Leader.PowerModThisBattle);
        Assert.Empty(state.Players[0].Hand);
        Assert.Single(state.Players[0].Trash);
    }
}
