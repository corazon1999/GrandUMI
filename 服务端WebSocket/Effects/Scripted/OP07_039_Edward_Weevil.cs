using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-039 爱德华·维布鲁（角色）
/// 【咚!!×1】【攻击时】确认我方卡组最上方的3张卡牌，自选顺序排列并放置到卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - 仅当此角色获得 1 张或以上被赋予中的咚!! 时才发动（【咚!!×1】前置）。
///   - 玩家从顶 3 张中选择若干张放到卡组最下方（其余仍按原相对顺序留在最上方）。
///     "自选顺序排列"简化为：留顶的牌保持原相对顺序，放底的牌按选择顺序放到最下方。
///   - 卡组牌为非公开区，通过 extra.choiceCards 下发卡面供客户端展示。
/// </summary>
public class OP07_039_Edward_Weevil : IScriptedEffect
{
    public string CardNumber => "OP07-039";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】：此角色需有至少 1 张被赋予中的咚!!
        if (me.AttachedDonCount(self.Id) < 1) return;

        int peek = Math.Min(3, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        // 选择要放到卡组最下方的卡牌（其余留在最上方）
        var toBottom = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReorderTop",
            "确认卡组顶 3 张：选择放到卡组最下方的卡牌（其余留在最上方）",
            top.Select(c => c.Id.ToString()).ToList(), 0, peek, extra);

        if (toBottom.Count == 0) return; // 全部留顶，顺序不变

        // 取出这 3 张
        foreach (var c in top) me.Deck.Remove(c);
        // 留顶的按原相对顺序放回最上方
        var keepTop = top.Where(c => !toBottom.Contains(c.Id.ToString())).ToList();
        // 放底的按玩家选择顺序排列
        var bottom = toBottom
            .Select(id => top.First(c => c.Id.ToString() == id))
            .ToList();

        // 先把留顶的按原顺序插回最上方
        for (int i = keepTop.Count - 1; i >= 0; i--) me.Deck.Insert(0, keepTop[i]);
        // 放底的追加到最下方
        me.Deck.AddRange(bottom);
    }
}
