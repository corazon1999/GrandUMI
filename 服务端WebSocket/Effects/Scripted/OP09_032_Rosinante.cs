using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-032 堂吉诃德·罗西南德（角色 / 时光旅诗·海军·堂吉诃德海盗团）
/// 【阻挡者】（纯关键词，由引擎处理）
/// 【对方的攻击时】【每回合1次】将此角色转为活跃状态。
///
/// 实现说明 / 简化点：
///   - 【阻挡者】为关键词，引擎自行处理，本脚本仅实现主动效果。
///   - 【对方的攻击时】每回合 1 次（用 TurnOnceUsed 约束），强制将自身转为活跃。
///     文本无"可以"，故无须 ConfirmOptional。
/// </summary>
public class OP09_032_Rosinante : IScriptedEffect
{
    public string CardNumber => "OP09-032";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-oppatk" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;

        AtomicOps.ActivateCard(self);
        me.TurnOnceUsed.Add(key);
        return Task.CompletedTask;
    }
}
