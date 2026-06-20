using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-008 小奥兹Jr.（角色 / 炎 / 巨人族·白胡子海盗团旗下）
/// 【每回合1次】此角色因效果将要被KO的场合，可以改为丢弃我方手牌中的1张事件或舞台卡牌，使此角色不会被KO。
///
/// 实现说明 / 简化点：
///   - 用 PreKO 钩子（将要被KO时派发，可拦截被KO卡自身）。仅拦截自身。
///   - "因效果"近似为非战斗结算中（CurrentBattle == null）；引擎不传 KO 来源。
///   - 成本：丢弃手牌中1张事件或舞台卡牌；支付后调用 MarkPreventKO 取消本次KO。
///   - 每回合1次。
/// </summary>
public class EB01_008_LittleOarsJr : IScriptedEffect
{
    public string CardNumber => "EB01-008";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.PreKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 近似"因效果"：非战斗结算中
        if (ctx.State.CurrentBattle is not null) return;

        var key = self.Info.Number + "-prevko" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本候选：手牌中事件或舞台卡牌
        var fodder = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Event || c.Info.Kind == CardKind.Stage).ToList();
        if (fodder.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "小奥兹Jr.：丢弃手牌中1张事件或舞台卡牌，使此角色不会被KO？");
        if (!use) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃手牌中1张事件或舞台卡牌",
            fodder.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count == 0) return;

        var dc = fodder.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.DiscardHand(me, dc);

        me.TurnOnceUsed.Add(key);
        ctx.State.MarkPreventKO(self.Id);
    }
}
