using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>对反馈中的规则边界保留实测，避免把正常规则改成错误行为。</summary>
public sealed class LatestFeedbackRules20261125Tests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task 巴奇领袖能登场十费巴奇但场上不足五张大角色时会自行沉底()
    {
        var s = TestScene.New("OP09-042").Build(); var me = s.Players[0];
        var cost = Card("OP09-048"); var buggy = Card("OP09-051"); me.Hand.AddRange([cost, buggy]);
        me.CostArea.AddRange(Enumerable.Range(0, 5).Select(_ => new DonCard { State = DonState.Active }));
        await EffectRuntime.Resolve(s, 0, me.Leader, EffectTrigger.ActivatedMain,
            new MockPromptService().QueueChoose(cost.Id.ToString()).QueueChoose(buggy.Id.ToString()));
        Assert.Contains(cost, me.Trash);
        Assert.Contains(buggy, me.Deck);
        Assert.Empty(me.Hand);
    }

    [Fact]
    public async Task 路飞减攻离场后再登场恢复原本力量()
    {
        var s = TestScene.New().MyCharacter("OP17-093").Build(); var me = s.Players[0]; var card = me.Characters[0];
        AtomicOps.AddPowerThisTurn(card, -5000);
        AtomicOps.BounceToHand(s, 0, card);
        await AtomicOps.PlayFromHandFree(s, 0, card);
        Assert.Equal(card.Info.Power, s.CurrentPowerOf(0, card));
    }

    [Fact]
    public async Task 三哥能检索原巴洛克但排除其他卡号的同名三哥()
    {
        var s = TestScene.New().MyCharacter("OP09-056").Build(); var me = s.Players[0];
        var allowed = Card("OP09-046"); var excluded = Card("OP16-056"); me.Deck.AddRange([allowed, excluded]);
        var prompts = new MockPromptService().QueueChoose(allowed.Id.ToString());
        await EffectRuntime.Resolve(s, 0, me.Characters[0], EffectTrigger.OnEnterField, prompts);
        var search = prompts.ChooseHistory.First();
        Assert.Contains(allowed.Id.ToString(), search.choices);
        Assert.DoesNotContain(excluded.Id.ToString(), search.choices);
        Assert.Contains(allowed, me.Hand);
    }

    [Fact]
    public async Task 第二张迷你梅利可再次发动并给同一角色再贴咚()
    {
        var s = TestScene.New().Build(); var me = s.Players[0];
        me.CostArea.AddRange(Enumerable.Range(0, 2).Select(_ => new DonCard { State = DonState.Rest }));
        foreach (int _ in Enumerable.Range(0, 2))
        {
            var stage = Card("OP18-078"); me.Hand.Add(stage); await AtomicOps.PlayFromHandFree(s, 0, stage);
            // 本例只验证启动主要；登场抽牌不参与这个边界。
            s.PendingEnterFields.Clear();
            await EffectRuntime.Resolve(s, 0, stage, EffectTrigger.ActivatedMain,
                new MockPromptService().QueueChoose(me.Leader.Id.ToString()));
            Assert.True(stage.IsTapped);
        }
        Assert.Equal(2, me.AttachedDonCount(me.Leader.Id));
    }

    [Fact]
    public async Task 七费佐罗主阶段可响应战斗后仍有八千力量的角色()
    {
        var s = TestScene.New().MyCharacter("EB04-007").OppCharacter("OP15-003").Build();
        var self = s.Players[0].Characters[0]; var opponent = s.Players[1].Characters[0];
        opponent.OriginalPowerOverride = 7000;
        await EffectRuntime.Resolve(s, 0, self, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.False(ActionValidator.HasKeyword(s, self, "登场回合可攻击角色"));
        AtomicOps.AddPowerThisTurn(opponent, 1000);
        await EffectRuntime.Resolve(s, 0, self, EffectTrigger.ActivatedMain, new MockPromptService());
        Assert.True(ActionValidator.HasKeyword(s, self, "登场回合可攻击角色"));
    }

    [Fact]
    public async Task 宣传邦妮不能自身触发领袖但可以作为贴咚目标()
    {
        var s = TestScene.New("OP13-100").MyCharacter("P-113").MyCharacter("OP13-113").Build(); var me = s.Players[0];
        me.CostArea.AddRange(Enumerable.Range(0, 2).Select(_ => new DonCard { State = DonState.Rest }));
        var prompts = new MockPromptService().QueueChoose(me.Characters[0].Id.ToString());
        await EffectRuntime.Resolve(s, 0, me.Leader, EffectTrigger.OnAllyCharEnter, prompts,
            new() { ["cardId"] = me.Characters[0].Id.ToString(), ["owner"] = 0 });
        Assert.Empty(prompts.ConfirmHistory);
        await EffectRuntime.Resolve(s, 0, me.Leader, EffectTrigger.OnAllyCharEnter, prompts,
            new() { ["cardId"] = me.Characters[1].Id.ToString(), ["owner"] = 0 });
        Assert.Equal(2, me.AttachedDonCount(me.Characters[0].Id));
    }
}
