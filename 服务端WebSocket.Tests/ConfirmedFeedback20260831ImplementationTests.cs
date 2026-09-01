using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

public sealed class ConfirmedFeedback20260831ImplementationTests
{
    private static CardInstance Card(string number)
        => new() { Info = GrandUMI.Cards.CardDatabase.Get(number)! };

    [Fact]
    public async Task OP17_012_仅在KO时从手牌登场合法的一费白胡子卡牌()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP17-012");
        var legalCharacter = Card("EB01-005");
        var legalStage = Card("OP16-021");
        var wrongCost = Card("OP17-012");
        var wrongTrait = Card("OP01-006");
        var eventCard = Card("OP17-017");
        me.Hand.AddRange([legalCharacter, legalStage, wrongCost, wrongTrait, eventCard]);
        var prompts = new MockPromptService().QueueChoose(legalCharacter.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);
        Assert.Empty(prompts.ChooseHistory);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO, prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal(0, prompt.min);
        Assert.Equal(1, prompt.max);
        Assert.Contains(legalCharacter.Id.ToString(), prompt.choices);
        Assert.Contains(legalStage.Id.ToString(), prompt.choices);
        Assert.DoesNotContain(wrongCost.Id.ToString(), prompt.choices);
        Assert.DoesNotContain(wrongTrait.Id.ToString(), prompt.choices);
        Assert.DoesNotContain(eventCard.Id.ToString(), prompt.choices);
        Assert.Contains(legalCharacter, me.Characters);
        Assert.DoesNotContain(legalCharacter, me.Hand);
        Assert.Empty(me.LifeArea);
    }

    [Fact]
    public async Task OP17_012_最多一张可选零张且所有区域保持不变()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var source = Card("OP17-012");
        var candidate = Card("OP17-010");
        var life = Card("OP01-006");
        me.Hand.Add(candidate);
        me.LifeArea.Add(life);
        var handBefore = me.Hand.Select(card => card.Id).ToArray();
        var prompts = new MockPromptService().QueueChooseEmpty();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnKO, prompts);

        Assert.Equal(handBefore, me.Hand.Select(card => card.Id));
        Assert.Equal([life], me.LifeArea);
        Assert.DoesNotContain(candidate, me.Characters);
        Assert.Null(me.StageCard);
    }

    [Fact]
    public async Task OP17_012_满场时先腾位再登场且绝不进入生命区()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        for (var index = 0; index < 5; index++) me.Characters.Add(Card("OP01-006"));
        var victim = me.Characters[3];
        var candidate = Card("OP17-010");
        var life = Card("OP01-010");
        me.Hand.Add(candidate);
        me.LifeArea.Add(life);
        var prompts = new MockPromptService()
            .QueueChoose(candidate.Id.ToString())
            .QueueChoose(victim.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP17-012"), EffectTrigger.OnKO, prompts);

        Assert.Equal(5, me.Characters.Count);
        Assert.Contains(candidate, me.Characters);
        Assert.DoesNotContain(victim, me.Characters);
        Assert.Contains(victim, me.Trash);
        Assert.Equal([life], me.LifeArea);
        Assert.Equal(["OwnHand", "OverflowTrash"], prompts.ChooseHistory.Select(item => item.kind));
    }

    [Fact]
    public async Task OP17_012_选择响应期间目标已离开手牌时不会重复移动实例()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var candidate = Card("OP17-010");
        me.Hand.Add(candidate);
        var prompts = new MoveChosenBeforeResponsePrompt(me, candidate);

        await EffectRuntime.Resolve(state, 0, Card("OP17-012"), EffectTrigger.OnKO, prompts);

        Assert.DoesNotContain(candidate, me.Hand);
        Assert.Contains(candidate, me.Trash);
        Assert.DoesNotContain(candidate, me.Characters);
        Assert.DoesNotContain(candidate, me.LifeArea);
        Assert.Null(me.StageCard);
    }

    private sealed class MoveChosenBeforeResponsePrompt(PlayerState owner, CardInstance chosen) : IPromptService
    {
        public Task<List<string>> ChooseCards(
            int playerIdx,
            string kind,
            string text,
            IReadOnlyList<string> validChoices,
            int min,
            int max,
            Dictionary<string, object?>? extra = null)
        {
            Assert.Contains(chosen.Id.ToString(), validChoices);
            Assert.True(owner.Hand.Remove(chosen));
            owner.Trash.Add(chosen);
            return Task.FromResult(new List<string> { chosen.Id.ToString() });
        }

        public Task<bool> ConfirmOptional(int playerIdx, string text) => Task.FromResult(false);
        public Task<int> ChooseOption(int playerIdx, string text, IReadOnlyList<string> options,
            Dictionary<string, object?>? extra = null) => Task.FromResult(-1);
        public Task<bool> AskLifeTrigger(int playerIdx, CardInstance lifeCard, bool hasRealTrigger) => Task.FromResult(false);
    }
}
