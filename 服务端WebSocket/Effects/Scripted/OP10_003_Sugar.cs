using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-003 糖糖（领航 / 炎暗 4 费 5000，堂吉诃德海盗团）
/// 1.【我方的回合结束时】我方场上存在力量为6000或更高且拥有《堂吉诃德海盗团》特征的角色的场合，
///    将我方最多1张咚!!转为活跃状态。
/// 2.【对方的回合中】【每回合1次】当我方发动事件时，从咚!!卡组中追加最多1张活跃状态的咚!!。
///
/// 实现说明：
/// - 第一段：OnMyTurnEnd 时机，检查我方是否存在「当前力量≥6000 且含《堂吉诃德海盗团》」的角色，
///   满足则把最多 1 张休息状态的咚!!转为活跃。
/// - 第二段「当我方发动事件时」缺乏对应的事件发动钩子（EffectTrigger 无『对方回合中我方打出事件』
///   触发项），引擎暂无该机制，故此段未实现（仅省略对己方有利的追加咚收益，不影响合法性）。
/// </summary>
public class OP10_003_Sugar : IScriptedEffect
{
    public string CardNumber => "OP10-003";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnMyTurnEnd;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;

        // 条件：我方场上存在力量≥6000且含《堂吉诃德海盗团》特征的角色
        bool ok = me.Characters.Any(c =>
            c.Info.HasKeyword("堂吉诃德海盗团") &&
            ctx.State.CurrentPowerOf(owner, c) >= 6000);
        if (!ok) return Task.CompletedTask;

        // 将我方最多 1 张休息状态的咚!!转为活跃状态
        foreach (var d in me.CostArea)
        {
            if (d.State == DonState.Rest)
            {
                d.State = DonState.Active;
                d.AttachedToCardId = null;
                break;
            }
        }
        return Task.CompletedTask;
    }
}
