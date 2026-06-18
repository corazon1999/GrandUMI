using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-119 马歇尔·D·提奇（角色）
/// 【登场时】确认我方卡组最上方的3张卡牌，将其中最多1张卡牌加入生命区最上方。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现：探顶3张（choiceCards 给全3张让玩家看全），选≤1张加入生命区最上方，余按原序放回卡组底。
///   原 DSL 占位 AddLifeFromDeck 是"无脑顶1张入生命"、缺玩家从3张里选，故改脚本。
/// </summary>
public class OP16_119_Teach : IScriptedEffect
{
    public string CardNumber => "OP16-119";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int peek = Math.Min(3, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
            "确认卡组顶3张，将其中最多1张加入生命区最上方",
            top.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = top.First(c => c.Id.ToString() == chosen[0]);
            me.Deck.Remove(picked);
            me.LifeArea.Insert(0, picked); // 加入生命区最上方
        }

        // 剩余仍在顶部的卡按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
