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
///   - "此领袖无法进行攻击" 由卡表的基础禁攻能力统一进入动作校验与公开快照；
///     整卡效果被无效时，该自身不利持续效果也随之失效。
///   - 成本"将 1 张咚!! 转为休息状态"：将费用区中 1 张活跃咚置为 Rest。
///   - 成本“将生命区最上方 1 张翻至正面朝上”：要求生命顶为背面，支付后保持正面朝上。
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

        // 成本前置：至少要有 1 张活跃咚，且生命顶须能翻至正面。
        if (me.ActiveDonCount < 1 || me.LifeArea.Count == 0 || me.LifeArea[0].IsLifeFaceUp) return;

        // 候选收益：手牌中费用 ≤ 场上咚!! 总张数、且《海王类》或名为"梅迦罗"的角色
        int donCount = me.TotalDonInCostArea;
        var candidates = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            ctx.State.CurrentCostOf(c) <= donCount &&
            (c.Info.HasKeyword("海王类") || c.MatchesName("梅迦罗"))
        ).ToList();
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "白星【启动主要】：将 1 张咚!! 转为休息状态并将生命顶翻至正面，之后可登场最多 1 张符合条件的角色？");
        if (!use) return;

        // 支付成本：将 1 张活跃咚转为休息
        var don = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (don is null) return;
        don.State = DonState.Rest;
        AtomicOps.FlipTopLifeFaceUp(me);

        me.TurnOnceUsed.Add(key);

        // “最多1张”允许手牌中没有符合条件的角色时仍只支付成本发动。
        if (candidates.Count == 0) return;

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
            await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
