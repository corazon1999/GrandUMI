using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-116 人造恶魔果实SMILE（事件 / 暗 / 百兽海盗团・SMILE，cost2）
/// 【主要】确认我方卡组最上方的5张卡牌，将其中最多1张费用不高于3且拥有《SMILE》特征的角色卡牌登场。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明：
///   - EventMain；确认卡组顶5张，公开（让玩家选）最多1张费用≤3且拥有《SMILE》特征的角色，
///     通过统一免费登场入口处理登场限制、满场腾位和登场时队列。
///   - 无合法目标也确认完整牌面；剩余卡牌自选顺序放底，不改变顶5张之外的牌序。
/// </summary>
public class OP01_116_SMILE : IScriptedEffect
{
    public string CardNumber => "OP01-116";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var cand = top.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 3 &&
            c.Info.HasKeyword("SMILE")).ToList();

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
            "确认顶5张，将最多1张费用≤3且拥有《SMILE》特征的角色登场",
            cand.Select(c => c.Id.ToString()).ToList(), 0, Math.Min(1, cand.Count), extra);
        var picked = chosen.Select(id => cand.FirstOrDefault(card => card.Id.ToString() == id)).FirstOrDefault();
        if (picked is not null && me.Deck.Contains(picked))
            await AtomicOps.PlayFromDeckFree(ctx.State, ctx.OwnerIndex, picked);

        // 剩余卡牌自选顺序放底；兼容返回空顺序，不能丢失或重复卡牌。
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        var ordered = rest;
        if (rest.Count > 1)
        {
            var order = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReorderToDeckBottom",
                "将剩余卡牌自选顺序放回卡组最下方（先选的牌在较上方）",
                rest.Select(card => card.Id.ToString()).ToList(), 0, rest.Count,
                new Dictionary<string, object?>
                {
                    ["choiceCards"] = rest.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
                    ["allowDefaultOrder"] = true,
                });
            ordered = order.Select(id => rest.FirstOrDefault(card => card.Id.ToString() == id))
                .Where(card => card is not null).Cast<CardInstance>().DistinctBy(card => card.Id).ToList();
            ordered.AddRange(rest.Where(card => !ordered.Contains(card)));
        }
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(ordered);
    }
}
