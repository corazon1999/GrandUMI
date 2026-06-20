using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-016 蒙奇·D·戈普（角色）
/// 【登场时】我方领袖为"萨波"或"波特夹斯·D·艾斯"或"蒙奇·D·路飞"的场合，
///   确认我方卡组最上方的 4 张卡牌，公开其中最多 1 张费用为 3 或更高的卡牌并加入手牌。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明 / 简化点：
///   - 领袖名"或"条件（3 选 1）DSL 的 if 节只能 AND，无法表达，故用脚本。
///   - 探顶逻辑复刻 DslInterpreter.LookTopRevealImpl：choiceCards = 确认到的全部 4 张（让玩家看全），
///     可公开子集（validChoices）= 其中费用≥3 的卡。
///   - "费用为 3 或更高"按原始费用 Info.Cost 判断。
///   - "自选顺序放回卡组最下方"简化为按确认时的原相对顺序放回底部（与既有探顶实现一致）。
/// </summary>
public class OP13_016_Gomp : IScriptedEffect
{
    public string CardNumber => "OP13-016";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 领袖名为 萨波 / 波特夹斯·D·艾斯 / 蒙奇·D·路飞 之一
        var leader = me.Leader.Info;
        if (!leader.NameIs("萨波") && !leader.NameIs("波特夹斯·D·艾斯") && !leader.NameIs("蒙奇·D·路飞"))
            return;

        int peek = Math.Min(4, me.Deck.Count);
        if (peek == 0) return;

        var top = me.Deck.Take(peek).ToList();
        // 可公开加入手牌的子集：费用为 3 或更高
        var candidates = top.Where(c => c.Info.Cost >= 3).ToList();

        if (candidates.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                // 检索卡规范：choiceCards = 确认到的全部 top（让玩家看全身份）
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组顶 4 张，公开最多 1 张费用为 3 或更高的卡牌并加入手牌",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            foreach (var cid in chosen)
            {
                var picked = candidates.FirstOrDefault(c => c.Id.ToString() == cid);
                if (picked is not null) { me.Deck.Remove(picked); me.Hand.Add(picked); }
            }
        }

        // 剩余仍在顶部的卡按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
