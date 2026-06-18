using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-026 小阳光（角色 / 风 1 费 2000，FILM/草帽一伙）
/// 【启动主要】【每回合1次】可以将我方的1张咚!!转为休息状态：
///   直到下个对方的回合结束时为止，此角色的力量+2000。
///
/// 实现：
///   - 成本：把我方 1 张活跃状态的咚!! 转为休息状态（脚本自行支付，引擎不代扣）。
///     无活跃咚时无法支付，直接返回。
///   - 每回合 1 次：用 me.TurnOnceUsed 标记。
///
/// 简化点（引擎限制，已在 summary 注明）：
///   - "直到下个对方的回合结束时为止 +2000" 的精确时效引擎无对应字段
///     （PowerModThisTurn 会在我方本回合结束阶段就被清除，过早；
///      PowerModPersistent 永不自动清除，过久）。
///     此处采用 PowerModThisTurn，效果在我方本回合内有效（攻防收益保留），
///     但无法持续到对方回合结束。这是引擎缺乏"UntilNextOpponentEndPhase 力量修正"
///     字段导致的简化。
/// </summary>
public class OP13_026_Sunny : IScriptedEffect
{
    public string CardNumber => "OP13-026";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 每回合 1 次
        var key = "OP13-026-Activated" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;

        // 成本：将我方 1 张活跃状态的咚!! 转为休息状态
        var activeDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (activeDon is null) return Task.CompletedTask;
        activeDon.State = DonState.Rest;

        me.TurnOnceUsed.Add(key);

        // 此角色力量 +2000（简化为本回合，见类注释）
        AtomicOps.AddPowerThisTurn(ctx.Source, 2000);

        return Task.CompletedTask;
    }
}
