using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-069 特拉法尔加·罗（角色，暗）
/// 【攻击时】对方场上咚!!的张数多于我方场上咚!!的张数的场合，确认我方卡组最上方的 5 张卡牌，
///   公开其中最多 1 张拥有《心脏海盗团》特征的卡牌并加入手牌。之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明：
///   - “对方场上咚!!多于我方”用费用区咚!!总数比较：opp.TotalDonInCostArea &gt; me.TotalDonInCostArea。
///   - 看顶 5 张，公开最多 1 张《心脏海盗团》加入手牌；其余按原相对顺序放回卡组最下方。
///   - 卡组牌为非公开区，prompt 传 extra.choiceCards 以展示全部 5 张卡面。
/// </summary>
public class OP05_069_TrafalgarLaw : IScriptedEffect
{
    public string CardNumber => "OP05-069";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (opp.TotalDonInCostArea <= me.TotalDonInCostArea) return;

        int k = Math.Min(5, me.Deck.Count);
        if (k == 0) return;
        var top = me.Deck.Take(k).ToList();

        var cand = top.Where(c => c.Info.HasKeyword("心脏海盗团")).ToList();
        if (cand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组顶 5 张，公开最多 1 张《心脏海盗团》加入手牌",
                cand.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (ch.Count > 0)
            {
                var p = cand.First(c => c.Id.ToString() == ch[0]);
                me.Deck.Remove(p);
                me.Hand.Add(p);
            }
        }

        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
