using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-066 乔拉（角色 / 堂吉诃德海盗团）
/// 【对方的攻击时】【每回合1次】可以将我方的 2 张咚!!转为休息状态：
///   将对方最多 1 张费用不高于 4 的角色转为休息状态。
///
/// 实现说明 / 简化点：
///   - 成本为可选（"可以"）：需要至少 2 张活跃咚，支付为 2 张活跃咚 → 休息。
///   - 每回合 1 次，用 TurnOnceUsed 控制。
///   - "转为休息状态"= IsTapped = true（RestCard）。
/// </summary>
public class OP10_066_Giolla : IScriptedEffect
{
    public string CardNumber => "OP10-066";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-oppatk" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：至少 2 张活跃咚
        int activeDon = me.CostArea.Count(d => d.State == DonState.Active);
        if (activeDon < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "乔拉【对方的攻击时】：将我方 2 张咚!!转为休息状态，将对方 1 张费用≤4 的角色转为休息？");
        if (!use) return;

        // 支付成本：2 张活跃咚 → 休息
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 2) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; rested++; }
        }
        if (rested < 2) return;

        me.TurnOnceUsed.Add(key);

        var cands = opp.Characters.Where(c => c.Info.Cost <= 4).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择最多 1 张费用≤4 的对方角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
