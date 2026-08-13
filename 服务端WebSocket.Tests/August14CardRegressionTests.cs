using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-08-14 玩家反馈的卡牌效果与离场置换回归测试。</summary>
public class August14CardRegressionTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP14_018_满足条件时可以将反击力量赋予角色()
    {
        var state = TestScene.New().MyCharacter("OP15-003").OppCharacter("OP15-008").Build();
        var target = state.Players[0].Characters.Single();
        var source = Card("OP14-018");
        var prompts = new MockPromptService().QueueChoose(target.Id.ToString());

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.EventCounter, prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OwnLeaderOrCharacter", prompt.kind);
        Assert.Contains(state.Players[0].Leader.Id.ToString(), prompt.choices);
        Assert.Contains(target.Id.ToString(), prompt.choices);
        Assert.Equal(4000, target.PowerModThisBattle);
        Assert.Equal(0, state.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task OP14_018_场上没有八千力量角色时不能获得反击加成()
    {
        var state = TestScene.New().MyCharacter("OP15-003").Build();
        var target = state.Players[0].Characters.Single();
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, Card("OP14-018"), EffectTrigger.EventCounter, prompts);

        Assert.Empty(prompts.ChooseHistory);
        Assert.Equal(0, target.PowerModThisBattle);
        Assert.Equal(0, state.Players[0].Leader.PowerModThisBattle);
    }

    [Fact]
    public async Task OP09_076_可以只选择一张咚放回而不必选满上限()
    {
        var state = TestScene.New().MyActiveDon(3).Build();
        var source = Card("OP09-076");
        state.Players[0].Characters.Add(source);
        state.Players[0].DonDeck.Add(new DonCard { State = DonState.InDeck });
        var returnedId = state.Players[0].CostArea[1].Id.ToString();
        var prompts = new MockPromptService().QueueChoose(returnedId);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, prompts);

        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("ReturnOwnDon", prompt.kind);
        Assert.Equal(0, prompt.min);
        Assert.Equal(3, prompt.max);
        Assert.True(prompt.extra?.TryGetValue("allowVariableReturnCount", out var value) == true
            && value is true);
        Assert.Equal(3, state.Players[0].CostArea.Count);
        Assert.Single(state.Players[0].DonDeck);
    }

    [Fact]
    public async Task OP06_058_同时放底两张拉布时只支付一次离场置换成本()
    {
        var state = TestScene.New().MyActiveDon(1).Build();
        var me = state.Players[0];
        var first = Card("OP15-035");
        var second = Card("OP15-035");
        me.Characters.AddRange([first, second]);
        var source = Card("OP06-058");
        var restDon = me.CostArea.Single();
        var prompts = new MockPromptService()
            .QueueChoose(first.Id.ToString(), second.Id.ToString())
            .QueueConfirm(true)
            .QueueChoose(me.Leader.Id.ToString(), restDon.Id.ToString());

        await EffectRuntime.Resolve(state, 1, source, EffectTrigger.EventMain, prompts);

        Assert.Contains(first, me.Characters);
        Assert.Contains(second, me.Characters);
        Assert.DoesNotContain(first, me.Deck);
        Assert.DoesNotContain(second, me.Deck);
        Assert.True(me.Leader.IsTapped);
        Assert.Equal(DonState.Rest, restDon.State);
        Assert.Single(prompts.ConfirmHistory);
        Assert.Single(prompts.ChooseHistory, prompt => prompt.kind == "RestOwnCardsOrDon");
        var targetPrompt = Assert.Single(prompts.ChooseHistory, prompt => prompt.kind == "OpponentCharacterCostLe6");
        Assert.Equal(2, targetPrompt.max);
    }

    [Fact]
    public async Task 同一效果批量离场时其他守护卡也只支付一次置换成本()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var first = Card("OP13-017");
        var second = Card("OP13-017");
        me.Characters.AddRange([first, second]);
        first.CostModThisTurn = -1;
        second.CostModThisTurn = -1;
        var source = Card("OP06-058");
        var prompts = new MockPromptService()
            .QueueChoose(first.Id.ToString(), second.Id.ToString())
            .QueueConfirm(true);

        await EffectRuntime.Resolve(state, 1, source, EffectTrigger.EventMain, prompts);

        Assert.Contains(first, me.Characters);
        Assert.Contains(second, me.Characters);
        Assert.Empty(me.Deck);
        Assert.Equal(-2000, first.PowerModThisTurn);
        Assert.Equal(0, second.PowerModThisTurn);
        Assert.Single(prompts.ConfirmHistory);
    }
}
