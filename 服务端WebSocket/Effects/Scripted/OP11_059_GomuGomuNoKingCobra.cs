using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-059 橡皮橡皮王蛇（事件）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +2000。
///   之后，我方手牌不多于 4 张的场合，本次战斗中，该卡牌的力量再 +2000。
/// 【触发】将最多 1 张费用不高于 2 的角色放回其持有者的手牌。
///
/// 实现说明：
///   - 反击：选 1 张我方领袖/角色 +2000；若选中后我方手牌 ≤4 张，则对同一目标再 +2000。
///     （选完成本后手牌数量按当前手牌计算；本事件已结算离手。）
///   - 触发：回手对象包含双方任意费用 ≤2 的角色。
/// </summary>
public class OP11_059_GomuGomuNoKingCobra : IScriptedEffect
{
    public string CardNumber => "OP11-059";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            var targets = new List<CardInstance> { me.Leader };
            targets.AddRange(me.Characters);

            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "选择最多 1 张我方领袖或角色，本次战斗 +2000",
                targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count == 0) return;

            var tgt = targets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisBattle(tgt, 2000);

            // 之后：手牌不多于 4 张时，同一目标再 +2000
            if (me.Hand.Count <= 4)
                AtomicOps.AddPowerThisBattle(tgt, 2000);
            return;
        }

        // 【触发】将最多 1 张费用不高于 2 的角色放回其持有者的手牌
        var bounceCands = new List<(int owner, CardInstance card)>();
        foreach (var c in me.Characters.Where(c => c.Info.Cost <= 2))
            bounceCands.Add((ctx.OwnerIndex, c));
        foreach (var c in opp.Characters.Where(c => c.Info.Cost <= 2))
            bounceCands.Add((1 - ctx.OwnerIndex, c));
        if (bounceCands.Count == 0) return;

        var bpick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多 1 张费用不高于 2 的角色放回其持有者的手牌",
            bounceCands.Select(t => t.card.Id.ToString()).ToList(), 0, 1);
        if (bpick.Count == 0) return;

        var sel = bounceCands.First(t => t.card.Id.ToString() == bpick[0]);
        AtomicOps.BounceToHand(ctx.State, sel.owner, sel.card);
    }
}
