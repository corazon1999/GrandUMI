using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-055 Mr.2·盆·岁末(本萨姆)（角色，因佩尔地狱/原巴洛克工作室，力量1000）
/// 【登场时】抽取1张卡牌。
/// 【咚!!×1】【攻击时】本回合中，此角色原本的力量变为与对方领袖力量相同。
///
/// 实现：
///   - OnEnterField：抽1。
///   - OnAttackDeclare：门槛【咚×1】(被赋予咚≥1)；用 CardInstance.OriginalPowerOverride 把"原本力量"
///     本回合性设为对方领袖当前力量（回合末由 TurnEngine.ClearTurnScopedState 清除）。
///     注意是覆盖 base power，本卡自身被赋予的咚加成仍在其上叠加（符合"原本力量变为X"语义）。
/// </summary>
public class OP16_055_Bentham : IScriptedEffect
{
    public string CardNumber => "OP16-055";
    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return Task.CompletedTask;
        }

        // OnAttackDeclare：【咚!!×1】
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return Task.CompletedTask;
        int oppIdx = 1 - ctx.OwnerIndex;
        var oppLeader = ctx.State.Players[oppIdx].Leader;
        ctx.Source.OriginalPowerOverride = ctx.State.CurrentPowerOf(oppIdx, oppLeader);
        return Task.CompletedTask;
    }
}
