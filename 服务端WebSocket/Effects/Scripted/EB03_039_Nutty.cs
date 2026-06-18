using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-039 润媞（角色 / 地 / 百兽海盗团 cost6 power7000）
/// 【登场时】我方领袖拥有《百兽海盗团》特征的场合，抽取1张卡牌，丢弃我方的1张手牌。
///   之后，将我方废弃区中最多1张力量不高于6000且原本没有效果的角色卡牌登场。
///
/// 实现说明：
///   - "原本没有效果"近似判定为卡面无主动能力（Abilities 与 EffectTags 均为空）。
///   - 登场流程：先抽 1，再让玩家选 1 张手牌丢弃，最后从废弃区可选登场 1 张符合条件角色。
/// </summary>
public class EB03_039_Nutty : IScriptedEffect
{
    public string CardNumber => "EB03-039";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Leader.Info.HasKeyword("百兽海盗团")) return;

        // 抽 1
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

        // 丢弃 1 张手牌
        if (me.Hand.Count > 0)
        {
            var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
                "丢弃 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (disc.Count > 0)
            {
                var card = me.Hand.First(c => c.Id.ToString() == disc[0]);
                AtomicOps.DiscardHand(me, card);
            }
        }

        // 废弃区登场：力量≤6000 且 原本没有效果 的角色
        var cands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Power <= 6000 &&
            c.Info.Abilities.Length == 0 && c.Info.EffectTags.Length == 0
        ).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
            "将废弃区中最多 1 张力量≤6000 且原本没有效果的角色登场",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
