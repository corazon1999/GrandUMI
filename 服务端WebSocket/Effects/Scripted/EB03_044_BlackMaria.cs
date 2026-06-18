using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-044 黑色玛利亚（角色 / 地 / 百兽海盗团 cost3 power4000）
/// 我方领袖为多种颜色的场合，此角色获得【阻挡者】效果。（持续/条件 → GrantKeyword）
/// 【登场时】确认我方卡组最上方的 5 张卡牌，公开其中最多 1 张“鬼之岛”并加入手牌。
///   之后，将剩余的卡牌自选顺序放回卡组最下方，并将我方手牌中最多 1 张“鬼之岛”登场。
///
/// 实现说明：
///   - 阻挡者授予：注册 ContinuousEffect.GrantKeyword="阻挡者"，Predicate 限自身且领袖为多色
///     （Info.ColorList.Length >= 2）。
///   - 登场段：探顶 5 张，公开最多 1 张“鬼之岛”加入手牌，余者按原相对顺序放回卡组最下方；
///     再将手牌中最多 1 张“鬼之岛”登场（PlayFromHandFree 支持舞台卡登场，替换已有舞台）。
/// </summary>
public class EB03_044_BlackMaria : IScriptedEffect
{
    public string CardNumber => "EB03-044";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        // 持续/条件：领袖为多色时，此角色获得【阻挡者】
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].Leader.Info.ColorList.Length >= 2,
        });

        // 【登场时】探顶 5 张
        int peek = Math.Min(5, me.Deck.Count);
        if (peek > 0)
        {
            var top = me.Deck.Take(peek).ToList();
            var revealCands = top.Where(c => c.Info.NameContains("鬼之岛")).ToList();
            if (revealCands.Count > 0)
            {
                var extra = new Dictionary<string, object?>
                {
                    ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                };
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                    "公开最多 1 张“鬼之岛”加入手牌",
                    revealCands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
                if (chosen.Count > 0)
                {
                    var picked = revealCands.First(c => c.Id.ToString() == chosen[0]);
                    me.Deck.Remove(picked);
                    me.Hand.Add(picked);
                }
            }

            // 剩余按原相对顺序放回卡组最下方
            var rest = top.Where(c => me.Deck.Contains(c)).ToList();
            foreach (var c in rest) me.Deck.Remove(c);
            me.Deck.AddRange(rest);
        }

        // 之后：将手牌中最多 1 张“鬼之岛”登场
        var island = me.Hand.Where(c => c.Info.NameContains("鬼之岛")).ToList();
        if (island.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = island.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandStage",
                "将手牌中最多 1 张“鬼之岛”登场",
                island.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (ch.Count > 0)
            {
                var p = island.First(c => c.Id.ToString() == ch[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, p);
            }
        }
    }
}
