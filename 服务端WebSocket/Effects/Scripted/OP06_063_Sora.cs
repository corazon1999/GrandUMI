using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-063 温思默克·索拉（角色）
/// 【登场时】可以丢弃我方的 1 张手牌：我方场上咚!!的张数不多于对方场上咚!!的张数的场合，
/// 将我方废弃区中最多 1 张力量不高于 4000 且拥有《温思默克家》特征的角色卡牌加入手牌。
///
/// 实现说明：
/// - "我方咚数不多于对方咚数" = 双方费用区咚总数比较（含活跃/休息/赋予中），用 CostArea.Count 比较。
/// - 弃 1 手牌为发动成本（"可以"=可选）；为避免误判成本，只有满足条件且废弃区有候选时才询问。
/// - 候选来自废弃区（非公开区展示需求不强，但加 choiceCards 方便客户端展示卡面）。
/// </summary>
public class OP06_063_Sora : IScriptedEffect
{
    public string CardNumber => "OP06-063";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方咚数 ≤ 对方咚数
        if (me.CostArea.Count > opp.CostArea.Count) return;

        // 废弃区候选：力量≤4000 且《温思默克家》特征的角色
        var cands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Power <= 4000 &&
            c.Info.HasKeyword("温思默克家")).ToList();
        if (cands.Count == 0) return;

        // 至少要有 1 张手牌可弃，否则无法支付成本
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "索拉【登场时】：丢弃 1 张手牌，从废弃区将最多 1 张力量≤4000 的《温思默克家》角色加入手牌？");
        if (!use) return;

        // 成本：丢弃我方 1 张手牌
        var discard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张手牌作为成本",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (discard.Count < 1) return; // 未支付成本
        var dc = me.Hand.First(c => c.Id.ToString() == discard[0]);
        AtomicOps.DiscardHand(me, dc);

        // 效果：从废弃区将最多 1 张候选加入手牌
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
            "从废弃区将最多 1 张力量≤4000 的《温思默克家》角色加入手牌",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.TrashToHand(me, picked);
        }
    }
}
