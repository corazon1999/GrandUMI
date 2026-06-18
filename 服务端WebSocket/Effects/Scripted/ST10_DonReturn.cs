using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST10-006 蒙奇·D·路飞（10费角色）
/// 【速攻】【每回合1次】当对方发动【阻挡者】效果时，将对方最多1张力量不高于8000的角色KO。
/// </summary>
public class ST10_006_Luffy : IScriptedEffect
{
    public string CardNumber => "ST10-006";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppBlocker;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var key = $"ST10-006-oppblocker:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key)) return;
        var cands = ctx.State.Players[oppIdx].Characters.Where(c => ctx.State.CurrentPowerOf(oppIdx, c) <= 8000).ToList();
        if (cands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "KO 对方最多1张力量≤8000 的角色", cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        me.TurnOnceUsed.Add(key);
        if (chosen.Count == 0) return;
        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.KO(ctx.State, oppIdx, tgt);
    }
}

/// <summary>
/// ST10-007 基拉（角色）
/// 【我方的回合中】【每回合1次】当我方场上的咚!!放回咚!!卡组时，将对方最多1张处于休息状态且费用不高于3的角色KO。
/// </summary>
public class ST10_007_Killer : IScriptedEffect
{
    public string CardNumber => "ST10-007";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;
        var key = $"ST10-007-donreturn:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key)) return;
        int oppIdx = 1 - ctx.OwnerIndex;
        var cands = ctx.State.Players[oppIdx].Characters.Where(c => c.IsTapped && c.Info.Cost <= 3).ToList();
        if (cands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCharacter",
            "KO 对方最多1张休息且费用≤3 的角色", cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        me.TurnOnceUsed.Add(key);
        if (chosen.Count == 0) return;
        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.KO(ctx.State, oppIdx, tgt);
    }
}

/// <summary>
/// ST10-011 火特（角色）
/// 【我方的回合中】【每回合1次】当我方场上的咚!!放回咚!!卡组时，直到下个我方的回合开始时为止，此角色的力量+2000。
/// </summary>
public class ST10_011_Heat : IScriptedEffect
{
    public string CardNumber => "ST10-011";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;
        var key = $"ST10-011-donreturn:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        AtomicOps.AddPowerPersistent(ctx.Source, 2000);
        me.TurnOnceUsed.Add(key);
        return Task.CompletedTask;
    }
}

/// <summary>
/// ST10-014 瓦亚（角色）
/// 【阻挡者】【每回合1次】当我方场上的咚!!放回咚!!卡组时，抽取1张卡牌，丢弃1张手牌。
/// </summary>
public class ST10_014_Wire : IScriptedEffect
{
    public string CardNumber => "ST10-014";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = $"ST10-014-donreturn:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        if (me.Hand.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand", "丢弃1张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (card is not null) AtomicOps.DiscardHand(me, card);
    }
}
