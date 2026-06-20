using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-069 斗鱼（角色 / 动物 / 德莱斯罗兹）
/// 【咚!!×1】【攻击时】咚!!-1（将我方场上 1 张咚!!放回咚!!卡组）：
///   将对方最多 1 张费用不高于 1 的角色KO。
///
/// 实现说明 / 简化点：
///   - 【咚!!×1】为发动条件：自身需被赋予至少 1 张咚!!。
///   - 成本"咚!!-1"为强制成本（文本无"可以"），但触发节无法表达可选支付，此处按惯例：
///     条件满足且玩家有 KO 收益时直接支付（场上至少 1 张咚可放回），否则不发动。
///   - "费用不高于 1"取卡面原始费用 c.Info.Cost。
/// </summary>
public class OP10_069_Betty : IScriptedEffect
{
    public string CardNumber => "OP10-069";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】发动条件：自身贴有至少 1 张咚!!
        if (me.AttachedDonCount(self.Id) < 1) return;

        // 成本：咚!!-1，需要场上有可放回的咚
        int returnable = me.CostArea.Count;
        if (returnable < 1) return;

        var cands = opp.Characters.Where(c => c.Info.Cost <= 1).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "斗鱼【攻击时】：咚!!-1，将对方 1 张费用≤1 的角色KO？");
        if (!use) return;

        // 支付成本：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择最多 1 张费用≤1 的对方角色KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
