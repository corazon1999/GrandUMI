using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>OP08-036 与 OP14-031 的卡效回归测试。</summary>
public class OP08_OP14RegressionTests
{
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

    [Theory]
    [InlineData("OP14-110")]
    [InlineData("OP14-111")]
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
}
