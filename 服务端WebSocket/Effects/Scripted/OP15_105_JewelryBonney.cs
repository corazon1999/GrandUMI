using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-105 杰丽·邦妮（角色 / 光）
/// 我方原本的力量不高于7000的角色因对方的效果将要离开场上的场合，可以改为将我方生命区最上方的1张卡牌加入手牌，使该角色不离场。
/// </summary>
public class OP15_105_JewelryBonney : IScriptedEffect
{
    public string CardNumber => "OP15-105";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var vOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int vi ? vi : -1;
        if (vOwner != ctx.OwnerIndex || vId is null) return;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == vId);
        if (victim is null || victim.Info.Power > 7000) return;
        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"杰丽·邦妮：将我方生命顶1张加入手牌，使「{victim.Info.Name}」不离场？");
        if (!use) return;
        var top = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        me.Hand.Add(top);
        ctx.State.MarkPreventLeave(victim.Id);
    }
}
