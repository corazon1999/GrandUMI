using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public class OP14_118_EffectTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task Counter_WithTwoOrLessLife_PreventsOnlyAnActiveOpponentCharacterFromAttacking()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        me.LifeArea.AddRange([Card("OP15-050"), Card("OP15-051")]);
        var activeCharacter = Card("OP15-050");
        var restedCharacter = Card("OP15-051");
        restedCharacter.IsTapped = true;
        opponent.Characters.AddRange([activeCharacter, restedCharacter]);
        var source = Card("OP14-118");
        int leaderPowerBefore = state.CurrentPowerOf(0, me.Leader);
        var prompts = new MockPromptService().QueueChoose(activeCharacter.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);

        var restriction = Assert.Single(activeCharacter.Restrictions);
        Assert.Equal(RestrictionKind.CannotAttack, restriction.Kind);
        Assert.Equal(KeywordDuration.ThisTurn, restriction.Duration);
        Assert.Empty(restedCharacter.Restrictions);
        Assert.Equal(leaderPowerBefore, state.CurrentPowerOf(0, me.Leader));
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Contains(activeCharacter.Id.ToString(), prompt.choices);
        Assert.DoesNotContain(restedCharacter.Id.ToString(), prompt.choices);
    }

    [Fact]
    public async Task Counter_WithThreeLife_DoesNotApplyAnEffect()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        me.LifeArea.AddRange([Card("OP15-050"), Card("OP15-051"), Card("OP15-052")]);
        var target = Card("OP15-050");
        opponent.Characters.Add(target);
        var source = Card("OP14-118");
        int leaderPowerBefore = state.CurrentPowerOf(0, me.Leader);
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);

        Assert.Empty(target.Restrictions);
        Assert.Empty(prompts.ChooseHistory);
        Assert.Equal(leaderPowerBefore, state.CurrentPowerOf(0, me.Leader));
    }
}
