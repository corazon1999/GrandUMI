using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-056 贝加班克（角色 / 光 / 科学家・艾格赫德，cost5 power0）
/// 【阻挡者】（关键词，引擎处理）
/// 【登场时】确认我方卡组最上方的 5 张卡牌，将其中最多 1 张"贝加班克"以外的费用不高于 5 且拥有
///   《科学家》特征的角色卡牌登场。之后，将剩余的卡牌自选顺序放回卡组最下方，
///   对方场上的角色不多于 2 张的场合，丢弃我方的 1 张手牌。
///
/// 实现说明：
///   - 确认卡组顶 5 张（向发动者展示候选卡面）。可登场候选 = 角色 且 费用≤5 且《科学家》且 非"贝加班克"。
///   - 从卡组登场：先把选中卡临时移入手牌再 PlayFromHandFree（复用入场逻辑，等价从卡组免费登场）。
///   - 剩余顶部卡按原相对顺序放回卡组最下方。
///   - 之后：对方角色 ≤2 张时，丢弃我方 1 张手牌（由玩家选弃）。
/// </summary>
public class EB02_056_Vegapunk : IScriptedEffect
{
    public string CardNumber => "EB02-056";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        int peek = Math.Min(5, me.Deck.Count);
        if (peek > 0)
        {
            var top = me.Deck.Take(peek).ToList();

            var cands = top.Where(c =>
                c.Info.Kind == CardKind.Character &&
                c.Info.Cost <= 5 &&
                c.Info.HasKeyword("科学家") &&
                !c.Info.NameContains("贝加班克")
            ).ToList();

            if (cands.Count > 0)
            {
                var extra = new Dictionary<string, object?>
                {
                    ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                };
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                    "确认卡组顶 5 张，将其中最多 1 张费用≤5 的《科学家》角色登场",
                    cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
                if (chosen.Count > 0)
                {
                    var picked = cands.First(c => c.Id.ToString() == chosen[0]);
                    me.Deck.Remove(picked);
                    // 从卡组登场：临时入手后复用免费登场逻辑
                    me.Hand.Add(picked);
                    AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
                }
            }

            // 剩余仍在顶部的卡牌按原相对顺序放回卡组最下方
            var rest = top.Where(c => me.Deck.Contains(c)).ToList();
            foreach (var c in rest) me.Deck.Remove(c);
            me.Deck.AddRange(rest);
        }

        // 之后：对方角色不多于 2 张 → 丢弃我方 1 张手牌
        if (opp.Characters.Count <= 2 && me.Hand.Count > 0)
        {
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "丢弃我方 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (pick.Count > 0)
            {
                var d = me.Hand.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.DiscardHand(me, d);
            }
        }
    }
}
