using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-020 罗罗诺亚·佐罗（领航）
/// 【咚!!×3】【启动主要】【每回合1次】本回合中，此领袖与对方的角色进行战斗的场合，将此领袖转为活跃状态。
/// 之后，本回合中，此领袖无法攻击对方原本的费用不高于7的角色。
/// 实现：战斗结束时由引擎记录“本回合已与角色战斗”；之后发动启动主要时立即转为活跃，
/// 并从该时点起设置 NoAttackCostLeThisTurn=7。
/// </summary>
public class OP12_020_Zoro : IScriptedEffect
{
    public string CardNumber => "OP12-020";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source; // 领袖自身

        // 【咚!!×3】发动条件：自身被赋予咚 ≥ 3。不足时明确报错而非静默（反馈#168"点了没反应"）
        if (me.AttachedDonCount(self.Id) < 3)
        {
            ctx.Engine?.SendError(ctx.OwnerIndex, "需要为此领袖赋予 3 张或更多咚!!才能发动");
            return Task.CompletedTask;
        }

        // 【每回合1次】：key 须符合快照约定 "{番号}-act:{id}"，否则 leaderActivatedUsedThisTurn
        // 恒 false、按钮用后不消失，再点永远静默（反馈#168 根因）
        var key = "OP12-020-act:" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        if (!self.BattledOpponentCharacterThisTurn)
        {
            ctx.Engine?.SendError(ctx.OwnerIndex, "本回合此领袖尚未与对方角色进行战斗");
            return Task.CompletedTask;
        }

        me.TurnOnceUsed.Add(key);
        self.IsTapped = false;
        self.NoAttackCostLeThisTurn = 7;
        return Task.CompletedTask;
    }
}
