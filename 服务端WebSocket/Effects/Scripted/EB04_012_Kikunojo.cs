using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-012 菊之丞（角色 / 风 / 和之国·赤鞘九人男）
/// 【启动主要】【每回合1次】在此角色登场的回合的场合，将我方拥有《和之国》特征的领袖转为活跃状态。
///
/// 实现说明：
///   - 条件「此角色登场的回合」= 本卡的 TurnPlayed 等于当前回合数 TurnCount。
///   - 仅当我方领袖含《和之国》特征时执行；用 ActivateCard 解除领袖休息状态。
///   - 每回合1次用 TurnOnceUsed。
/// </summary>
public class EB04_012_Kikunojo : IScriptedEffect
{
    public string CardNumber => "EB04-012";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 条件：此角色登场的回合
        if (self.TurnPlayed != ctx.State.TurnCount) return Task.CompletedTask;

        // 条件：我方领袖含《和之国》
        if (!me.Leader.Info.HasKeyword("和之国")) return Task.CompletedTask;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        AtomicOps.ActivateCard(me.Leader);
        return Task.CompletedTask;
    }
}
