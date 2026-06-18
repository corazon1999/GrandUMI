using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-020 罗罗诺亚·佐罗（领航）
/// 【咚!!×3】【启动主要】【每回合1次】本回合中，此领袖与对方的角色进行战斗的场合，将此领袖转为活跃状态。
/// 之后，本回合中，此领袖无法攻击对方原本的费用不高于7的角色。
/// 实现：ReactivateAfterBattleThisTurn(战斗后重激活，引擎在 EndBattle 处理) + NoAttackCostLeThisTurn=7。
/// </summary>
public class OP12_020_Zoro : IScriptedEffect
{
    public string CardNumber => "OP12-020";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source; // 领袖自身

        // 【咚!!×3】发动条件：自身被赋予咚 ≥ 3
        if (me.AttachedDonCount(self.Id) < 3) return Task.CompletedTask;

        // 【每回合1次】
        var key = "OP12-020-Activated" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        self.ReactivateAfterBattleThisTurn = true;   // 与对方角色战斗后转为活跃
        self.NoAttackCostLeThisTurn = 7;             // 本回合无法攻击对方原费用≤7角色
        return Task.CompletedTask;
    }
}
