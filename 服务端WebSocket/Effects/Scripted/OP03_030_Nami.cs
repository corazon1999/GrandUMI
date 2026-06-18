using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-030 奈美（角色）
/// 【登场时】确认我方卡组最上方的5张卡牌，公开其中最多1张"奈美"以外的拥有《东海》特征的绿色卡牌
///   并加入手牌。之后，将剩余的卡牌自选顺序放回卡组最下方。
/// 【触发】此卡牌登场。（由引擎/DSL 处理触发登场，本脚本仅处理登场时检索）
///
/// 实现说明：
///   - 看顶5张；候选 = 绿色(ColorList 含"草") + 含《东海》特征 + 名称非"奈美"。
///   - 颜色用 ColorList 判定。其余按原相对顺序放回卡组底。
/// </summary>
public class OP03_030_Nami : IScriptedEffect
{
    public string CardNumber => "OP03-030";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int k = Math.Min(5, me.Deck.Count);
        if (k == 0) return;
        var top = me.Deck.Take(k).ToList();

        var cand = top.Where(c =>
            c.Info.ColorList.Contains("草") &&
            c.Info.HasKeyword("东海") &&
            !c.MatchesName("奈美")).ToList();

        if (cand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "公开最多1张\"奈美\"以外的《东海》绿色卡牌加入手牌",
                cand.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (ch.Count > 0)
            {
                var p = cand.First(c => c.Id.ToString() == ch[0]);
                me.Deck.Remove(p);
                me.Hand.Add(p);
            }
        }

        // 剩余按原相对顺序放回卡组最下方
        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
