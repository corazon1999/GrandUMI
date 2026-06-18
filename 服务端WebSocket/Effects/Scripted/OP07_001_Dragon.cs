using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-001 蒙奇·D·龙（领袖 / 炎 / 5000 / 革命军）
/// 【启动主要】【每回合1次】赋予我方1张角色合计最多2张被赋予中的咚!!。
///
/// 说明 / 简化点：
///   - 从费用区取活跃咚赋予给所选角色，最多 2 张（受当前活跃咚数量限制）。
///   - "合计最多2张"理解为本次最多赋予 2 张活跃咚给同一张角色。
/// </summary>
public class OP07_001_Dragon : IScriptedEffect
{
    public string CardNumber => "OP07-001";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = "OP07-001-main" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        if (me.Characters.Count == 0) return;

        int available = me.CostArea.Count(d => d.State == DonState.Active);
        if (available <= 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张我方角色，赋予最多 2 张被赋予中的咚!!",
            me.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = me.Characters.First(c => c.Id.ToString() == chosen[0]);
        int n = Math.Min(2, available);
        AtomicOps.AttachDonFromCost(me, target.Id, n, DonState.Active);

        me.TurnOnceUsed.Add(key);
    }
}
