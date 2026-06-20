using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-009 舒莱亚（角色）
/// 【阻挡者】（由引擎处理关键词，本脚本不实现）
/// 【攻击时】/【阻挡时】【每回合1次】直到下个我方的回合开始时为止，
///   此角色原本的力量变为与对方领袖力量相同。
///
/// 实现说明 / 简化点：
///   - 仅实现主动效果部分；【阻挡者】关键词由引擎统一处理。
///   - 【每回合1次】用 TurnOnceUsed 去重（攻击时与阻挡时共享同一计数）。
///   - "原本的力量变为与对方领袖力量相同"：用 SetPowerThisTurn 将本回合力量设为对方领袖的当前力量。
///     传 donAttached=0，使写入的 PowerModThisTurn 仅相对"本卡基础力量+已有修正"对齐到目标值，
///     战斗中本卡贴附的咚!! 仍在其上叠加（与"原本力量变为X、咚!!另加"的文本意图一致）。
///   - "直到下个我方回合开始时"以本回合力量近似（与同套既有卡 OP06-030 一致）。
/// </summary>
public class OP06_009_Shura : IScriptedEffect
{
    public string CardNumber => "OP06-009";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnAttackDeclare || t == EffectTrigger.OnBlockDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合 1 次（攻击时 / 阻挡时共享）
        var key = self.Info.Number + "-setpower" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;

        if (opp.Leader is null) return Task.CompletedTask;

        me.TurnOnceUsed.Add(key);

        // 目标值：对方领袖的当前力量（含持续修正与贴咚）
        int targetPower = ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, opp.Leader);

        bool myTurn = ctx.State.CurrentTurnPlayer == ctx.OwnerIndex;
        AtomicOps.SetPowerThisTurn(self, targetPower, 0, myTurn);

        return Task.CompletedTask;
    }
}
