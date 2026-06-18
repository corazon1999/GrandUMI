using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-064 Ms.全周日（角色 / 暗 / 巴洛克工作室）
/// 【登场时】从咚!!卡组中追加最多 1 张休息状态的咚!!。
///   之后，我方场上存在 6 张或更多咚!!的场合，抽取 1 张卡牌。
///
/// 实现说明：
///   - 先 RefreshDonFromDeck(me,1,Rest) 追加 1 张休息咚。
///   - 之后判我方费用区咚总数（TotalDonInCostArea）≥6 则抽 1。
/// </summary>
public class OP04_064_MissAllSunday : IScriptedEffect
{
    public string CardNumber => "OP04-064";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 追加最多 1 张休息状态的咚!!
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);

        // 之后：场上存在 6 张或更多咚!! → 抽 1
        if (me.TotalDonInCostArea >= 6)
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

        return Task.CompletedTask;
    }
}
