using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-036 佩罗娜（角色，风）
/// 【我方的回合中】【每回合1次】当角色因我方的效果转为休息状态时，
///   将我方最多 1 张咚!! 转为活跃状态。
///
/// 实现说明 / 简化点：
///   - 通过反应式 watcher OnCharRested 触发（仅在我方回合）。文本为"当(任意)角色因我方
///     效果转为休息状态时"，故不限定被横置者为本卡自身，去掉 restedId==self 判断。
///   - 【每回合1次】用 me.TurnOnceUsed 记录，防止同一回合多次触发。
///   - "将咚!!转为活跃"为收益：从费用区选 1 张休息(Rest)状态的咚!! 置为活跃(Active)。
///     "最多 1 张"按惯例自动选择 1 张休息咚激活（无休息咚则无收益）。
/// </summary>
public class OP10_036_Perona : IScriptedEffect
{
    public string CardNumber => "OP10-036";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharRested;

    public Task Resolve(EffectContext ctx)
    {
        // 攻击/阻挡宣言的横置(reason=attack/block)不触发——本卡为「因效果转为休息」类（#227 引擎补派发后须显式过滤；reason 缺席=旧效果派发路径，放行）
        if (ctx.Vars.TryGetValue("reason", out var __restReason) && __restReason as string != "effect") return Task.CompletedTask;

        int owner = ctx.OwnerIndex;
        // 仅【我方的回合中】生效
        if (ctx.State.CurrentTurnPlayer != owner) return Task.CompletedTask;

        var me = ctx.State.Players[owner];

        // 【每回合1次】
        var key = ctx.Source.Info.Number + "-rest-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;

        // 将我方最多 1 张休息状态的咚!! 转为活跃状态
        var don = me.CostArea.FirstOrDefault(d => d.State == DonState.Rest);
        if (don is null) return Task.CompletedTask;

        don.State = DonState.Active;
        me.TurnOnceUsed.Add(key);
        return Task.CompletedTask;
    }
}
