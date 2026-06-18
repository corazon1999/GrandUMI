using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-118 蒙奇·D·路飞（角色）
/// 每回合 1 次，此角色不会因对方的效果而被KO。
/// 【攻击时】可以将我方废弃区中的 3 张卡牌自选顺序放回卡组最下方：
///   对方手牌为 5 张或更多的场合，对方丢弃其 1 张手牌。
///
/// 实现说明 / 简化点：
///   - "不会因对方效果被KO（每回合1次）"通过 PreKO 钩子拦截：每回合首次将要被KO时调用 MarkPreventKO 取消。
///     引擎无法区分 KO 来源是否为"对方效果"，故对一切首次 KO 触发拦截（含战斗），属合理近似。
///   - 攻击时成本：将废弃区 3 张放回卡组最下方（自选顺序近似为原序放底）。
/// </summary>
public class OP10_118_Luffy : IScriptedEffect
{
    public string CardNumber => "OP10-118";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.PreKO || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.PreKO)
        {
            var key = self.Info.Number + "-noKO" + ":" + self.Id;
            if (me.TurnOnceUsed.Contains(key)) return;
            me.TurnOnceUsed.Add(key);
            ctx.State.MarkPreventKO(self.Id);
            return;
        }

        // ── 【攻击时】 ──
        if (me.Trash.Count < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞【攻击时】：将废弃区3张卡牌放回卡组最下方，使对方（手牌≥5时）丢弃1张手牌？");
        if (!use) return;

        // 成本：从废弃区选 3 张放回卡组最下方
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Trash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrash",
            "选择废弃区的3张卡牌放回卡组最下方",
            me.Trash.Select(c => c.Id.ToString()).ToList(), 3, 3, extra);
        if (chosen.Count < 3) return;

        foreach (var id in chosen)
        {
            var card = me.Trash.First(c => c.Id.ToString() == id);
            me.Trash.Remove(card);
            me.Deck.Add(card);
        }

        // 收益：对方手牌≥5 时丢弃其 1 张手牌（由对方选择丢弃）
        if (opp.Hand.Count >= 5 && ctx.Engine is not null)
        {
            await AtomicOps.OpponentDiscardChosen(ctx.Engine, 1 - ctx.OwnerIndex, 1);
        }
    }
}
