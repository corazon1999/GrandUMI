using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-100 闪电（角色 / 黄(光) / 4 费 / 5000 / 革命军）
/// 【咚!!×1】【攻击时】将对方最多 1 张费用不高于双方生命卡牌合计张数的角色转为休息状态。
/// 【触发】我方领袖拥有《革命军》特征且双方生命卡牌合计张数不多于 5 张的场合，此卡牌登场。
///
/// 说明 / 简化点：
///   - 【咚!!×1】= 此角色须被赋予 ≥1 张咚（脚本内判定）。
///   - 阈值"双方生命卡牌合计张数"为动态值 = 我方生命 + 对方生命，脚本可直接计算。
///     注：此处按角色"当前费用"(CurrentCost，含费用修正)与阈值比较。
///   - 【触发】为条件登场（trigger play），由引擎处理，主动效果只实现【攻击时】部分。
/// </summary>
public class OP10_100_Lightning : IScriptedEffect
{
    public string CardNumber => "OP10-100";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】：此角色须被赋予 ≥1 张咚
        if (me.AttachedDonCount(self.Id) < 1) return;

        int threshold = me.LifeCount + opp.LifeCount;

        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= threshold).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            $"将对方最多 1 张费用≤{threshold}（双方生命合计）的角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.RestCard(tgt);
    }
}
