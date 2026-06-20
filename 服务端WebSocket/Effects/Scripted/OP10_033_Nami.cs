using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-033 奈美（角色）
/// 【登场时】我方场上存在 2 张或更多处于休息状态且拥有《时光旅诗》特征的角色的场合，
///   对方最多 1 张处于休息状态的咚!! 在下个对方的重置阶段不会转为活跃状态。
///
/// 实现说明：
///   - 条件判定：我方角色中 IsTapped 且特征含《时光旅诗》的数量 ≥ 2。
///   - 效果：对对方费用区中一张处于休息状态(DonState.Rest)的咚!! 设置
///     CannotActivateNextReset=true，引擎在对方下个重置阶段会跳过其激活（Wave3）。
/// </summary>
public class OP10_033_Nami : IScriptedEffect
{
    public string CardNumber => "OP10-033";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方场上 ≥2 张休息状态且拥有《时光旅诗》特征的角色
        int restedJourney = me.Characters.Count(c => c.IsTapped && c.Info.HasKeyword("时光旅诗"));
        if (restedJourney < 2) return Task.CompletedTask;

        // 效果：对方 1 张休息状态的咚!! 在下个对方重置阶段不会转为活跃
        var don = opp.CostArea.FirstOrDefault(d => d.State == DonState.Rest);
        if (don != null) don.CannotActivateNextReset = true;

        return Task.CompletedTask;
    }
}
