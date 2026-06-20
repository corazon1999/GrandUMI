using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-002 蒙奇·D·戈普（领航 / 炎·地）
/// 【我方的回合中】当赋予此领袖或我方角色咚!!时，本回合中，对方最多1张费用不高于7的角色费用-1。
///
/// 实现：监听 OnDonAttached（赋予咚后由 HandleAttachDon 派发，payload owner=赋予方、targetId=受咚卡）。
/// 仅我方回合、且赋予发生在我方场上（owner==自己）时：选对方最多1张当前费用≤7角色，本回合费用-1。
/// </summary>
public class OP02_002_Garp : IScriptedEffect
{
    public string CardNumber => "OP02-002";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonAttached;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;                       // 我方回合中
        var owner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (owner != ctx.OwnerIndex) return;                                             // 赋予发生在我方

        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 7).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多1张费用≤7的角色，本回合费用-1",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;
        var tgt = cands.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (tgt is not null) AtomicOps.AddCostModifier(tgt, -1, KeywordDuration.ThisTurn);
    }
}
