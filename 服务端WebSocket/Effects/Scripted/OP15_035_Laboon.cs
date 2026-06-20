using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-035 拉布（角色 / 风）
/// 我方原本的力量不高于7000的角色因对方的效果将要离开场上的场合，可以改为将我方的2张卡牌转为休息状态，使该角色不离场。
/// "2张卡牌"= 活跃的 领袖/角色/舞台/咚!!（走 AtomicOps.PromptRestOwnCards 混选）。
/// </summary>
public class OP15_035_Laboon : IScriptedEffect
{
    public string CardNumber => "OP15-035";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var vId = ctx.Vars.TryGetValue("victimId", out var vv) ? vv as string : null;
        var vOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int vi ? vi : -1;
        if (vOwner != ctx.OwnerIndex || vId is null) return;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == vId);
        if (victim is null || victim.Info.Power > 7000) return;
        if (AtomicOps.RestableCount(me) < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"拉布：将我方2张卡牌转为休息状态，使「{victim.Info.Name}」不离场？");
        if (!use) return;
        if (!await AtomicOps.PromptRestOwnCards(ctx, 2,
            "将我方 2 张卡牌转为休息状态（成本，可选活跃 领袖/角色/舞台/咚!!）")) return;
        ctx.State.MarkPreventLeave(victim.Id);
    }
}
