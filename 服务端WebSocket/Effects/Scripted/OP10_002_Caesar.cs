using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-002 凯撒·库朗（领航 / 炎水 4 费 5000，科学家/班克禁区）
/// 【咚!!×2】【攻击时】可以将我方1张费用为2或更高且拥有《班克禁区》特征的角色，
///   放回其持有者的手牌：将对方最多1张力量不高于4000的角色KO。
///
/// 实现说明：
/// - 【咚!!×2】= 此卡（领航）需被赋予至少 2 张咚!! 才能发动 → 检查 AttachedDonCount(self.Id) ≥ 2。
/// - 【攻击时】= OnAttackDeclare。
/// - 「可以…」可选，先 ConfirmOptional；成本为将我方 1 张费用≥2 且含《班克禁区》的角色退回手牌
///   （与 KO 结果强耦合的可选成本，DSL 触发节无法表达，故脚本实现）。
/// - 收益：KO 对方最多 1 张力量≤4000 的角色。
/// </summary>
public class OP10_002_Caesar : IScriptedEffect
{
    public string CardNumber => "OP10-002";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×2】：自身被赋予的咚!! 需 ≥2
        if (me.AttachedDonCount(self.Id) < 2) return;

        // 成本候选：我方费用≥2 且含《班克禁区》特征的角色
        var costCands = me.Characters
            .Where(c => c.Info.Cost >= 2 && c.Info.HasKeyword("班克禁区"))
            .ToList();
        if (costCands.Count == 0) return;

        // 收益候选：对方力量≤4000的角色
        var koCands = opp.Characters
            .Where(c => ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, c) <= 4000)
            .ToList();
        if (koCands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "凯撒【攻击时】：将我方1张费用≥2的《班克禁区》角色退回手牌，KO对方1张力量≤4000的角色？");
        if (!use) return;

        // 支付成本：选 1 张我方角色退回手牌
        var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张费用≥2的《班克禁区》角色退回手牌（成本）",
            costCands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (costPick.Count == 0) return;
        var costCard = costCands.First(c => c.Id.ToString() == costPick[0]);
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, costCard);

        // 收益：KO 对方最多 1 张力量≤4000的角色
        var koPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方 1 张力量≤4000的角色KO（最多1张）",
            koCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (koPick.Count > 0)
        {
            var tgt = koCands.First(c => c.Id.ToString() == koPick[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
