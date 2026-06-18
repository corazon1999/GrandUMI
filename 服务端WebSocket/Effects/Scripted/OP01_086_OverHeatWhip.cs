using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-086 超绞线鞭击（事件 / 水 / 王下七武海·堂吉诃德海盗团）
/// 【反击】本次战斗中，我方最多1张领袖或角色力量+4000。
///   之后，将最多1张活跃状态且费用不高于3的角色放回持有者的手牌。
/// 【触发】将最多1张费用不高于4的角色放回其持有者的手牌。
///
/// 实现说明：
///   - 【反击】= EventCounter 时机；力量+4000 用 AddPowerThisBattle。
///   - "任意一方"的角色：合并双方场上角色为候选；放回手牌用 BounceToHand（按其所属方下标）。
///   - 【触发】(OnLifeRevealTrigger)：将最多1张费用≤4角色放回其持有者手牌。
/// </summary>
public class OP01_086_OverHeatWhip : IScriptedEffect
{
    public string CardNumber => "OP01-086";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 本次战斗中，我方最多1张领袖或角色 +4000
            var buffTargets = new List<CardInstance> { me.Leader };
            buffTargets.AddRange(me.Characters);
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "本次战斗中，我方最多1张领袖或角色力量+4000",
                buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count > 0)
            {
                var tgt = buffTargets.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.AddPowerThisBattle(tgt, 4000);
            }

            // 之后：将最多1张活跃状态且费用≤3的角色（任意一方）放回手牌
            await BounceChoice(ctx, me, opp, costLimit: 3, requireActive: true);
            return;
        }

        // 【触发】将最多1张费用≤4的角色（任意一方）放回手牌
        await BounceChoice(ctx, me, opp, costLimit: 4, requireActive: false);
    }

    private static async Task BounceChoice(EffectContext ctx, PlayerState me, PlayerState opp,
        int costLimit, bool requireActive)
    {
        var cands = new List<(CardInstance card, int owner)>();
        foreach (var c in me.Characters)
            if ((!requireActive || !c.IsTapped) && ctx.State.CurrentCostOf(ctx.OwnerIndex, c) <= costLimit)
                cands.Add((c, ctx.OwnerIndex));
        foreach (var c in opp.Characters)
            if ((!requireActive || !c.IsTapped) && ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= costLimit)
                cands.Add((c, 1 - ctx.OwnerIndex));
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多1张角色放回其持有者的手牌",
            cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);
            AtomicOps.BounceToHand(ctx.State, picked.owner, picked.card);
        }
    }
}
