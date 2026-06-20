using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-048 莉贝卡（角色 / 地 / 德莱斯罗兹 cost2 power0）
/// 【阻挡者】（关键词，由引擎处理）
/// 【登场时】确认我方卡组最上方的 5 张卡牌，公开其中最多 1 张拥有《德莱斯罗兹》特征的舞台卡牌并加入手牌。
///   之后，将剩余的卡牌自选顺序放回卡组最下方，并将我方手牌中最多 1 张费用为 1 且拥有《德莱斯罗兹》特征的
///   舞台卡牌登场。
///
/// 实现说明：
///   - 探顶 5 张，公开最多 1 张《德莱斯罗兹》舞台卡加入手牌，余者按原相对顺序放回卡组最下方。
///   - 再将手牌中最多 1 张费用为 1 且《德莱斯罗兹》的舞台卡登场（PlayFromHandFree 支持舞台登场）。
/// </summary>
public class EB03_048_Rebecca : IScriptedEffect
{
    public string CardNumber => "EB03-048";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 探顶 5 张
        int peek = Math.Min(5, me.Deck.Count);
        if (peek > 0)
        {
            var top = me.Deck.Take(peek).ToList();
            var revealCands = top.Where(c =>
                c.Info.Kind == CardKind.Stage && c.Info.HasKeyword("德莱斯罗兹")).ToList();
            if (revealCands.Count > 0)
            {
                var extra = new Dictionary<string, object?>
                {
                    ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                };
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                    "公开最多 1 张《德莱斯罗兹》舞台卡加入手牌",
                    revealCands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
                if (chosen.Count > 0)
                {
                    var picked = revealCands.First(c => c.Id.ToString() == chosen[0]);
                    me.Deck.Remove(picked);
                    me.Hand.Add(picked);
                }
            }

            var rest = top.Where(c => me.Deck.Contains(c)).ToList();
            foreach (var c in rest) me.Deck.Remove(c);
            me.Deck.AddRange(rest);
        }

        // 之后：将手牌中最多 1 张费用为 1 且《德莱斯罗兹》的舞台卡登场
        var stages = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Stage &&
            c.Info.Cost == 1 &&
            c.Info.HasKeyword("德莱斯罗兹")).ToList();
        if (stages.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = stages.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandStage",
                "将手牌中最多 1 张费用 1 的《德莱斯罗兹》舞台卡登场",
                stages.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (ch.Count > 0)
            {
                var p = stages.First(c => c.Id.ToString() == ch[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, p);
            }
        }
    }
}
