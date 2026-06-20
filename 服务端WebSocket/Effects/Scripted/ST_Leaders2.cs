using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST04-001 盖德（领航）
/// 【启动主要】【每回合1次】咚!!-7：将对方最多1张生命卡牌置于废弃区。
/// </summary>
public class ST04_001_Kaido : IScriptedEffect
{
    public string CardNumber => "ST04-001";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var key = "ST04-001-Activated" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        if (me.CostArea.Count < 7) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 7)) return;
        if (opp.LifeArea.Count > 0)
        {
            var top = opp.LifeArea[0];
            opp.LifeArea.RemoveAt(0);
            opp.Trash.Add(top);
        }
        me.TurnOnceUsed.Add(key);
    }
}

/// <summary>
/// ST08-001 蒙奇·D·路飞（领航）
/// 【我方的回合中】当角色被KO时，赋予此领袖最多1张休息状态的咚!!。
/// （OnKO 全局事件；仅我方回合生效）
/// </summary>
public class ST08_001_Luffy : IScriptedEffect
{
    public string CardNumber => "ST08-001";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;
        AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);
        return Task.CompletedTask;
    }
}

/// <summary>
/// ST10-002 蒙奇·D·路飞（领航）
/// 【启动主要】【每回合1次】我方场上不存在咚!!，或我方场上存在8张或更多咚!!的场合，从咚!!卡组中追加最多1张活跃状态的咚!!。
/// </summary>
public class ST10_002_Luffy : IScriptedEffect
{
    public string CardNumber => "ST10-002";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = "ST10-002-Activated" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        int don = me.TotalDonInCostArea;
        if (!(don == 0 || don >= 8)) return Task.CompletedTask;
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        me.TurnOnceUsed.Add(key);
        return Task.CompletedTask;
    }
}
