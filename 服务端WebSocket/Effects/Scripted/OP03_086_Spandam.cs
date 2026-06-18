using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-086 斯巴达姆（角色 / 地 / CP9）
/// 【登场时】我方领袖拥有的特征中包含&lt;CP&gt;的场合，确认卡组最上方的 3 张卡牌，
///   公开其中最多 1 张"斯巴达姆"以外的拥有的特征中包含&lt;CP&gt;的卡牌并加入手牌。
///   之后，将剩余的卡牌放置到废弃区。
///
/// 实现说明：
///   - 领袖条件：领袖任一特征字符串包含 "CP"（子串匹配，覆盖 CP9/CP0 等）。
///   - 看顶 3 张，候选为含 CP 特征且名称非"斯巴达姆"的卡；公开最多 1 张加入手牌。
///   - 剩余（含未选中的候选与其余）全部放置到废弃区。
/// </summary>
public class OP03_086_Spandam : IScriptedEffect
{
    public string CardNumber => "OP03-086";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        bool LeaderHasCp = me.Leader.Info.Keywords.Any(k => k.Contains("CP"));
        if (!LeaderHasCp) return;

        int peek = Math.Min(3, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var cand = top.Where(c =>
            !c.Info.NameContains("斯巴达姆") &&
            c.Info.Keywords.Any(k => k.Contains("CP"))).ToList();

        if (cand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "公开最多 1 张含<CP>特征（斯巴达姆以外）的卡牌加入手牌",
                cand.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cand.First(c => c.Id.ToString() == chosen[0]);
                me.Deck.Remove(picked);
                me.Hand.Add(picked);
            }
        }

        // 之后：剩余仍在顶部的卡牌全部放置到废弃区
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest)
        {
            me.Deck.Remove(c);
            me.Trash.Add(c);
        }
    }
}
