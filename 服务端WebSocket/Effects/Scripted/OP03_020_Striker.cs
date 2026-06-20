using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-020 STRIKER（舞台 / 炎 1 费，白胡子海盗团）
/// 【启动主要】②（将费用区中2张咚!!转为休息状态），可以将此舞台转为休息状态：
///   我方领袖为"波特夹斯·D·艾斯"的场合，确认我方卡组最上方的5张卡牌，公开最多1张事件并加入手牌。
///   之后，将剩余的卡牌自选顺序放回卡组最下方。
///
/// 实现说明：
///   - 仅领袖为"波特夹斯·D·艾斯"时本效果有意义，故仅在该前提下询问发动。
///   - 成本：将费用区2张活跃咚!!转为休息 + 将此舞台转为休息状态。舞台须为活跃。
///   - 检索：确认顶5张，公开最多1张事件加入手牌，其余按原相对顺序放底。
/// </summary>
public class OP03_020_Striker : IScriptedEffect
{
    public string CardNumber => "OP03-020";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 仅领袖为"波特夹斯·D·艾斯"时有意义
        if (!me.Leader.Info.NameContains("波特夹斯·D·艾斯")) return;

        // 成本前置：舞台须活跃，且有≥2张活跃咚!!
        if (self.IsTapped) return;
        var activeDons = me.CostArea
            .Where(d => d.State == DonState.Active && d.AttachedToCardId is null).ToList();
        if (activeDons.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "STRIKER【启动主要】：休息2张咚!!与此舞台，确认卡组顶5张并公开最多1张事件加入手牌？");
        if (!use) return;

        // 成本：休息2张活跃咚!!
        activeDons[0].State = DonState.Rest;
        activeDons[1].State = DonState.Rest;
        // 成本：将此舞台转为休息状态
        AtomicOps.RestCard(self);

        // 效果：确认卡组顶5张，公开最多1张事件加入手牌
        int k = Math.Min(5, me.Deck.Count);
        if (k == 0) return;
        var top = me.Deck.Take(k).ToList();

        var events = top.Where(c => c.Info.Kind == CardKind.Event).ToList();
        if (events.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组顶5张，公开最多1张事件加入手牌",
                events.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (ch.Count > 0)
            {
                var p = events.First(c => c.Id.ToString() == ch[0]);
                me.Deck.Remove(p);
                me.Hand.Add(p);
            }
        }

        var rest = top.Where(c => me.Deck.Contains(c)).ToList();
        foreach (var c in rest) me.Deck.Remove(c);
        me.Deck.AddRange(rest);
    }
}
