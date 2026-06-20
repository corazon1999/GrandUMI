using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-010 查特罗斯·西格里格斯(茶胡子)（角色）
/// 【攻击时】我方力量为 6000 或更高的角色不多于 1 张的场合，本回合中，此角色的力量 +1000。
///
/// 实现说明：
///   - 在攻击声明时统计我方力量 ≥6000 的角色数量（用含持续效果的当前力量评估）。
///   - 注意题述对象为"角色"，故仅统计 Characters，不计入领袖。
///   - 若该数量 ≤1，则本回合此角色力量 +1000。
/// </summary>
public class OP10_010_Chatleousse : IScriptedEffect
{
    public string CardNumber => "OP10-010";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        int highPowerCount = me.Characters.Count(c =>
            ctx.State.CurrentPowerOf(ctx.OwnerIndex, c) >= 6000);

        if (highPowerCount <= 1)
        {
            AtomicOps.AddPowerThisTurn(self, 1000);
        }

        return Task.CompletedTask;
    }
}
