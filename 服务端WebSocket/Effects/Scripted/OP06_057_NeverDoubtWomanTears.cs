using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-057 老子绝对不怀疑女人的眼泪！（事件 / 水 1 费）
/// 【主要】本回合中，我方最多 1 张领袖或角色力量 +1000。
///   之后，公开我方卡组最上方的 1 张卡牌，并将最多 1 张费用为 2 的角色卡牌登场，
///   剩余的卡牌放回卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - "公开最上方 1 张" → 取 Deck[0]，若是费用 2 角色则可选登场；
///     若未登场，则按玩家选择放回顶或底（用 ChooseOption）。
///   - "放回卡组最上方"即原位（不动）；"放回最下方"用 Deck.Remove + Add。
/// </summary>
public class OP06_057_NeverDoubtWomanTears : IScriptedEffect
{
    public string CardNumber => "OP06-057";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 本回合中，我方最多 1 张领袖或角色力量 +1000
        var buffTargets = new List<CardInstance> { me.Leader };
        buffTargets.AddRange(me.Characters);
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张领袖或角色，本回合力量 +1000",
            buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = buffTargets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisTurn(tgt, 1000);
        }

        // 之后：公开卡组最上方 1 张
        if (me.Deck.Count == 0) return;
        var topCard = me.Deck[0];

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = new[] { new { id = topCard.Id.ToString(), number = topCard.Info.Number } }.ToList(),
        };

        bool played = false;
        if (topCard.Info.Kind == CardKind.Character && topCard.Info.Cost == 2)
        {
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "公开卡组顶 1 张，若为费用 2 的角色可将其登场（最多 1 张）",
                new List<string> { topCard.Id.ToString() }, 0, 1, extra);
            if (ch.Count > 0)
            {
                // 经由手牌中转以复用登场逻辑（满场牺牲 / TurnPlayed 设置等）
                me.Deck.Remove(topCard);
                me.Hand.Add(topCard);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, topCard);
                played = true;
            }
        }

        // 若未登场，将该卡放回卡组最上方或最下方
        if (!played && me.Deck.Contains(topCard))
        {
            int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "将公开的卡牌放回卡组最上方还是最下方？", new[] { "最上方", "最下方" });
            if (where == 1)
            {
                me.Deck.Remove(topCard);
                me.Deck.Add(topCard);
            }
            // where==0 → 原位（卡组最上方），不动
        }
    }
}
