using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-036 妮古·罗宾（角色 / 暗 / 草帽一伙，cost3 power2000）
/// 【阻挡者】（关键词，由引擎处理）
/// 【KO时】咚!!-1：确认我方卡组最上方的3张卡牌，公开其中最多1张拥有《草帽一伙》特征的卡牌并加入手牌。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明：
///   - KO时为可选成本「咚!!-1」：需我方费用区咚!!总数 ≥1，先 ConfirmOptional，再 ReturnDonToDeck(me,1)。
///   - 确认卡组顶3张，公开最多1张《草帽一伙》加入手牌（LookTopReveal，向自己确认全部、仅草帽可选）。
///   - 剩余按原相对顺序放回卡组最下方（"自选顺序"简化为原序）。
/// </summary>
public class EB02_036_NicoRobin : IScriptedEffect
{
    public string CardNumber => "EB02-036";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本：咚!!-1（需 ≥1 张）
        if (me.TotalDonInCostArea < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "妮古·罗宾【KO时】：咚!!-1，确认卡组顶3张并公开最多1张《草帽一伙》加入手牌？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        int peek = Math.Min(3, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var cand = top.Where(c => c.Info.HasKeyword("草帽一伙")).ToList();
        if (cand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "公开最多1张《草帽一伙》加入手牌",
                cand.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cand.First(c => c.Id.ToString() == chosen[0]);
                me.Deck.Remove(picked);
                me.Hand.Add(picked);
            }
        }

        // 剩余按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
