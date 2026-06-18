using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-059 亚马逊（角色）
/// 【对方的攻击时】可以将此角色转为休息状态：
///   对方可以将其 1 张活跃状态的咚!! 放回咚!!卡组。
///   没有那样做的场合，本回合中，对方最多 1 张领袖或角色力量 -2000。
///
/// 实现说明：
///   - 发动成本：横置此角色（RestCard）。"可以"=可选，先 ConfirmOptional。
///   - 由对方决策："放回 1 张活跃咚" 或 "不放回"。用对对方发起的 ChooseOption 实现。
///   - 对方选择放回 → ReturnDonToDeck(opp, 1)（优先放回活跃咚）。
///   - 对方选择不放回 → 由我方选对方 1 张领袖或角色本回合 -2000。
/// </summary>
public class OP15_059_Amazon : IScriptedEffect
{
    public string CardNumber => "OP15-059";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 已横置则无法支付成本
        if (self.IsTapped) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "亚马逊：将此角色转为休息状态？（对方需放回1张活跃咚，否则对方1张领袖/角色本回合-2000）");
        if (!use) return;

        // 成本：横置此角色
        AtomicOps.RestCard(self);

        // 对方是否有活跃咚可供放回
        bool oppHasActiveDon = opp.CostArea.Any(d => d.State == DonState.Active);

        int oppChoice;
        if (oppHasActiveDon)
        {
            oppChoice = await ctx.Prompts.ChooseOption(1 - ctx.OwnerIndex,
                "亚马逊的效果：选择将 1 张活跃咚!! 放回咚!!卡组，或不放回（不放回则你1张领袖/角色本回合-2000）",
                new[] { "放回 1 张活跃咚!!", "不放回" });
        }
        else
        {
            // 无活跃咚可放回，视为"没有那样做"
            oppChoice = 1;
        }

        if (oppChoice == 0)
        {
            AtomicOps.ReturnDonToDeck(opp, 1);
            return;
        }

        // "没有那样做" → 对方最多 1 张领袖或角色本回合 -2000
        var targets = new List<CardInstance> { opp.Leader };
        targets.AddRange(opp.Characters);
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = targets.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "选择对方最多 1 张领袖或角色，本回合力量 -2000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (pick.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisTurn(tgt, -2000);
        }
    }
}
