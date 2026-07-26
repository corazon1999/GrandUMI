using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class CombatRuleCompletionTests
{
    static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP01_051_LocksOpponentCharacterTargets_OnlyWhileRestedWithDon()
    {
        var state = TestScene.New().Build();
        state.TurnCount = 2;
        state.CurrentTurnPlayer = 1;
        var kid = Card("OP01-051");
        var otherTarget = Card("ST30-006");
        var attacker = Card("ST30-007");
        kid.IsTapped = true;
        otherTarget.IsTapped = true;
        attacker.TurnPlayed = 1;
        state.Players[0].Characters.Add(kid);
        state.Players[0].Characters.Add(otherTarget);
        state.Players[1].Characters.Add(attacker);
        var attachedDon = new DonCard { State = DonState.Attached, AttachedToCardId = kid.Id };
        state.Players[0].CostArea.Add(attachedDon);

        await EffectRuntime.Resolve(state, 0, kid, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.True(ActionValidator.CanAttack(state, 1, attacker.Id, true, null).Ok);
        Assert.True(ActionValidator.CanAttack(state, 1, attacker.Id, false, kid.Id).Ok);
        Assert.False(ActionValidator.CanAttack(state, 1, attacker.Id, false, otherTarget.Id).Ok);

        attachedDon.State = DonState.Rest;
        attachedDon.AttachedToCardId = null;
        Assert.True(ActionValidator.CanAttack(state, 1, attacker.Id, false, otherTarget.Id).Ok);
    }

    [Fact]
    public async Task OP03_004_GainsRushWithDon_ButCannotAttackLeaderOnPlayTurn()
    {
        var state = TestScene.New().OppCharacter("ST30-006").Build();
        state.TurnCount = 2;
        var krieg = Card("OP03-004");
        krieg.TurnPlayed = 2;
        state.Players[0].Characters.Add(krieg);
        state.Players[1].Characters[0].IsTapped = true;

        await EffectRuntime.Resolve(state, 0, krieg, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.False(ActionValidator.HasKeyword(state, krieg, "速攻"));
        Assert.False(ActionValidator.CanAttack(state, 0, krieg.Id, false, state.Players[1].Characters[0].Id).Ok);

        state.Players[0].CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = krieg.Id,
        });
        Assert.True(ActionValidator.HasKeyword(state, krieg, "速攻"));
        Assert.True(ActionValidator.CanAttack(state, 0, krieg.Id, false, state.Players[1].Characters[0].Id).Ok);
        Assert.False(ActionValidator.CanAttack(state, 0, krieg.Id, true, null).Ok);

        krieg.TurnPlayed = 1;
        Assert.True(ActionValidator.CanAttack(state, 0, krieg.Id, true, null).Ok);
    }

    [Fact]
    public async Task OP03_008_IsProtectedFromSlashBattleKO_ButNotOtherProperties()
    {
        var slashState = TestScene.New().Build();
        slashState.CurrentTurnPlayer = 1;
        var slashBuggy = Card("OP03-008");
        var slashAttacker = Card("OP01-052");
        slashState.Players[0].Characters.Add(slashBuggy);
        slashState.Players[1].Characters.Add(slashAttacker);
        await EffectRuntime.Resolve(slashState, 0, slashBuggy, EffectTrigger.OnEnterField, new MockPromptService());
        BattleEngine.StartAttack(slashState, slashAttacker.Id, false, slashBuggy.Id);
        BattleEngine.PassBlock(slashState);
        BattleEngine.PassCounter(slashState);

        await BattleEngine.ResolveDamageAsync(slashState, new MockPromptService());

        Assert.Contains(slashBuggy, slashState.Players[0].Characters);
        Assert.DoesNotContain(slashBuggy, slashState.Players[0].Trash);

        var otherState = TestScene.New().Build();
        otherState.CurrentTurnPlayer = 1;
        var otherBuggy = Card("OP03-008");
        var otherAttacker = Card("OP11-002");
        otherState.Players[0].Characters.Add(otherBuggy);
        otherState.Players[1].Characters.Add(otherAttacker);
        await EffectRuntime.Resolve(otherState, 0, otherBuggy, EffectTrigger.OnEnterField, new MockPromptService());
        BattleEngine.StartAttack(otherState, otherAttacker.Id, false, otherBuggy.Id);
        BattleEngine.PassBlock(otherState);
        BattleEngine.PassCounter(otherState);

        await BattleEngine.ResolveDamageAsync(otherState, new MockPromptService());

        Assert.DoesNotContain(otherBuggy, otherState.Players[0].Characters);
        Assert.Contains(otherBuggy, otherState.Players[0].Trash);
    }

    [Theory]
    [InlineData("OP03-047")]
    [InlineData("OP03-051")]
    public async Task EastBlueDamageCards_MillSevenAfterTheirOwnAttackDealsLifeDamage(string number)
    {
        var state = TestScene.New().Build();
        var source = Card(number);
        state.Players[0].Characters.Add(source);
        state.Players[0].CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = source.Id,
        });
        for (int i = 0; i < 7; i++) state.Players[0].Deck.Add(Card("ST30-002"));
        var prompts = new MockPromptService().QueueConfirm(true);

        Assert.True(EffectRuntime.HasEffectForTrigger(source, EffectTrigger.OnDamageToLeader));
        await EffectRuntime.TriggerEvent(state, EffectTrigger.OnDamageToLeader, prompts,
            new Dictionary<string, object?>
            {
                ["attackerId"] = source.Id.ToString(),
                ["defenderOwner"] = 1,
            });

        Assert.Empty(state.Players[0].Deck);
        Assert.Equal(7, state.Players[0].Trash.Count);
    }

    [Fact]
    public async Task OP03_051_OnKO_StillMillsThree()
    {
        var state = TestScene.New().Build();
        var belmer = Card("OP03-051");
        state.Players[0].Trash.Add(belmer);
        for (int i = 0; i < 3; i++) state.Players[0].Deck.Add(Card("ST30-002"));

        await EffectRuntime.Resolve(state, 0, belmer, EffectTrigger.OnKO,
            new MockPromptService().QueueConfirm(true));

        Assert.Empty(state.Players[0].Deck);
        Assert.Equal(4, state.Players[0].Trash.Count);
    }
}
