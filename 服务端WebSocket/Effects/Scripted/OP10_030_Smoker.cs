using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-030 斯摩格（角色 / 风 / 班克禁区・海军）
/// 【流放】（此卡牌给予伤害的场合，不发动触发效果将该卡牌放置到废弃区）
/// 【启动主要】将我方最多 1 张咚!! 转为活跃状态。之后，本回合中，我方无法通过角色的效果将咚!! 转为活跃状态。
///
/// 实现范围：将我方最多 1 张休息状态的咚!! 转为活跃状态，并登记本回合中
/// “我方无法通过角色效果将咚!!转为活跃状态”的限制。该限制也会阻止本卡再次直起咚!!。
/// 【流放】由 LifeRevealManager 在伤害结算时统一处理。
/// </summary>
public class OP10_030_Smoker : IScriptedEffect
{
    public string CardNumber => "OP10-030";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 本卡第一次结算后建立的角色效果直咚限制，也阻止本卡在同一回合再次发动收益。
        if (ctx.State.NoActivateDonByCharacterEffectThisTurn.Contains(ctx.OwnerIndex))
            return Task.CompletedTask;

        // 将我方最多 1 张休息状态的咚!! 转为活跃状态
        var rest = me.CostArea.FirstOrDefault(d => d.State == DonState.Rest);
        if (rest is not null) rest.State = DonState.Active;

        ctx.State.NoActivateDonByCharacterEffectThisTurn.Add(ctx.OwnerIndex);

        return Task.CompletedTask;
    }
}
