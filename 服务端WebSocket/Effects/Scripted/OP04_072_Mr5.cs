using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-072 Mr.5(杰姆)（角色 / 暗 / 巴洛克工作室）
/// 【对方攻击时】【每回合1次】咚!!-2（将我方场上 2 张咚放回咚卡组），可以将此角色转为休息状态：
///   将对方最多 1 张费用不高于 4 的角色 KO。
///
/// 实现说明：
///   - 触发时机 = OnOppAttackDeclare（对方攻击时）。每回合 1 次用 TurnOnceUsed 标记。
///   - 成本：咚!!-2（ReturnDonToDeck 2）+ 横置此角色（RestCard）。"可以…"用 ConfirmOptional 先问。
///   - 需我方被赋予/活跃的咚合计 ≥2 才可支付咚-2；此角色须为活跃状态才可横置。
/// </summary>
public class OP04_072_Mr5 : IScriptedEffect
{
    public string CardNumber => "OP04-072";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本可支付性检查：咚卡组可放回 2 张 + 此角色需为活跃
        if (me.TotalDonInCostArea < 2) return;
        if (self.IsTapped) return;

        var cands = opp.Characters.Where(c => c.Info.Cost <= 4).ToList();

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "Mr.5【对方攻击时】：咚!!-2 并将此角色转为休息状态，KO 对方最多 1 张费用≤4 的角色？");
        if (!use) return;

        // 支付成本
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 2)) return;
        me.TurnOnceUsed.Add(key);
        AtomicOps.RestCard(self);

        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤4 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
