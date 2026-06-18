using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-022 白星（领航）
/// 此领袖无法进行攻击。
/// 【启动主要】【每回合1次】可以将我方的 1 张咚!! 转为休息状态，并将我方生命区最上方的 1 张卡牌翻至正面朝上：
///   将我方手牌中最多 1 张费用不高于我方场上咚!! 的张数且拥有《海王类》特征的角色卡牌或"梅迦罗"登场。
///
/// 实现说明 / 简化点：
///   - "此领袖无法进行攻击" 为持续被动；引擎的攻击限制（Restriction）按回合时长生效、无持续通道，
///     该被动暂不在脚本中强制实现（不影响本启动效果）。
///   - 成本"将 1 张咚!! 转为休息状态"：将费用区中 1 张活跃咚置为 Rest。
///   - 成本"将生命区最上方 1 张翻至正面朝上"：引擎生命牌无正/反面状态字段，无法表达，按惯例省略该成本，仅实现收益。
///   - 收益：登场手牌中费用 ≤ 我方场上咚!! 总张数（CostArea 全部咚）且《海王类》特征或名为"梅迦罗"的角色，最多 1 张。
///   - 每回合 1 次用 TurnOnceUsed 标记。
/// </summary>
public class OP11_022_Shirahoshi : IScriptedEffect
{
    public string CardNumber => "OP11-022";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本前置：至少要有 1 张活跃咚可转休息
        if (me.ActiveDonCount < 1) return;

        // 候选收益：手牌中费用 ≤ 场上咚!! 总张数、且《海王类》或名为"梅迦罗"的角色
        int donCount = me.TotalDonInCostArea;
        var candidates = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.CurrentCost() <= donCount &&
            (c.Info.HasKeyword("海王类") || c.MatchesName("梅迦罗"))
        ).ToList();
        if (candidates.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "白星【启动主要】：将 1 张咚!! 转为休息状态，登场 1 张费用≤场上咚数的《海王类》角色或\"梅迦罗\"？");
        if (!use) return;

        // 支付成本：将 1 张活跃咚转为休息
        var don = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (don is null) return;
        don.State = DonState.Rest;

        me.TurnOnceUsed.Add(key);

        // 收益：登场最多 1 张候选角色
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场 1 张费用≤场上咚数的《海王类》角色或\"梅迦罗\"（最多 1 张）",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = candidates.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
