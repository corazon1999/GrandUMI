using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-039 莉贝卡（领航）
/// 持续：此领袖无法攻击
/// 启动主要：自身转休息 + 把我方 1 张《德莱斯罗兹》角色放回手牌 → 从手牌选 1 张费用 3 的《德莱斯罗兹》角色登场
/// </summary>
public class OP15_039_Rebecca : IScriptedEffect
{
    public string CardNumber => "OP15-039";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Leader.IsTapped) return;

        // 选 1 张场上的《德莱斯罗兹》角色放回手牌（费用）
        var dressrosaChars = me.Characters
            .Where(c => c.Info.HasKeyword("德莱斯罗兹"))
            .ToList();
        if (dressrosaChars.Count == 0) return;

        var picked = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnDressrosaChar",
            "选择 1 张《德莱斯罗兹》角色放回手牌",
            dressrosaChars.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (picked.Count == 0) return;
        var bounceTgt = dressrosaChars.First(c => c.Id.ToString() == picked[0]);
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, bounceTgt);

        me.Leader.IsTapped = true;

        // 从手牌找费用 3 的《德莱斯罗兹》角色登场
        var handCandidates = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost == 3 && c.Info.HasKeyword("德莱斯罗兹"))
            .ToList();
        if (handCandidates.Count == 0) return;
        var picked2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDressrosaCost3",
            "选择 1 张费用 3 的《德莱斯罗兹》角色登场",
            handCandidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (picked2.Count > 0)
        {
            var card = handCandidates.First(c => c.Id.ToString() == picked2[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
        }
    }
}
