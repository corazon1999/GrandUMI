using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-020 霍迪·琼斯（领袖，风 5000，鱼人族/新鱼人海盗团）
/// 【启动主要】可以将此领袖转为休息状态：将对方最多 1 张费用不高于 3 的角色 或 最多 1 张咚!!
///   转为休息状态。之后，本回合中，我方无法通过我方的效果将生命卡牌加入手牌。
///
/// 实现说明：
///   - 成本"将此领袖转为休息状态"用脚本横置 ctx.Source。
///   - "角色 或 咚!!" 为二选一 → ChooseOption。
///       · 选角色：从对方费用≤3 的角色中选最多 1 张横置(IsTapped=true)。
///       · 选咚!!：将对方 1 张活跃咚转为休息状态。
///   - 简化点：文末"本回合中我方无法通过效果将生命入手"属引擎未列机制（非力量持续修正），
///     无持续通道，本脚本仅实现横置收益、未实现该自我限制（与"只实现收益"惯例一致）。
/// </summary>
public class OP06_020_HodyJones : IScriptedEffect
{
    public string CardNumber => "OP06-020";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 领袖须为活跃状态才能横置作为成本
        if (self.IsTapped) return;

        var charCands = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        bool hasActiveDon = opp.CostArea.Any(d => d.State == DonState.Active);
        if (charCands.Count == 0 && !hasActiveDon) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "霍迪·琼斯【启动主要】：将此领袖转为休息状态，横置对方 1 张费用≤3 的角色或 1 张咚!!？");
        if (!use) return;

        // 成本：横置领袖自身
        AtomicOps.RestCard(self);

        // 二选一
        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "选择其一", new[] { "横置对方 1 张费用≤3 的角色", "横置对方 1 张咚!!" });

        if (opt == 0)
        {
            if (charCands.Count == 0) return;
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张费用≤3 的角色转为休息状态",
                charCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count > 0)
            {
                var tgt = charCands.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.RestCard(tgt);
            }
        }
        else
        {
            // 将对方 1 张活跃咚转为休息状态
            foreach (var d in opp.CostArea)
            {
                if (d.State == DonState.Active) { d.State = DonState.Rest; break; }
            }
        }
    }
}
