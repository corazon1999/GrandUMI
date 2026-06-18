using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-114 X·德雷克（角色）
/// 【启动主要】可以将此角色转为休息状态：我方生命卡牌的张数不多于对方生命卡牌的张数的场合，
///   将对方最多 1 张费用不高于 4 的角色转为休息状态。
///
/// 实现说明：
///   - 成本 = 将此角色转为休息状态（RestCard）。只有自身处于活跃状态时才可发动。
///   - 条件 = 我方生命张数 ≤ 对方生命张数（直接读 LifeCount 比较）。
///   - 收益 = 将对方最多 1 张费用≤4 的角色横置（RestCard）。
///   - 无可横置目标时不提示发动，避免白白横置自身。
/// </summary>
public class OP10_114_XDrake : IScriptedEffect
{
    public string CardNumber => "OP10-114";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本前提：自身需为活跃状态才能转为休息
        if (self.IsTapped) return;

        // 条件：我方生命张数不多于对方生命张数
        if (me.LifeCount > opp.LifeCount) return;

        // 收益候选：对方费用≤4 的活跃/任意角色
        var cands = opp.Characters.Where(c => c.Info.Cost <= 4).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "X·德雷克【启动主要】：将此角色转为休息状态，将对方最多 1 张费用≤4 的角色转为休息状态？");
        if (!use) return;

        // 支付成本：横置自身
        AtomicOps.RestCard(self);

        // 收益：横置对方最多 1 张费用≤4 的角色
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张费用≤4 的角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var target = cands.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
            if (target is not null)
                AtomicOps.RestCard(target);
        }
    }
}
