using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-067 佐罗十郎（角色，暗）
/// 【攻击时】我方的生命卡牌不多于 3 张的场合，从咚!!卡组中追加最多 1 张活跃状态的咚!!。
///
/// 实现说明：
///   - 条件 = 我方生命区张数 ≤ 3（me.LifeCount）。
///   - 满足条件时从咚!!卡组追加 1 张活跃状态咚!!（RefreshDonFromDeck，受咚卡组余量/上限约束）。
/// </summary>
public class OP05_067_Zorojuro : IScriptedEffect
{
    public string CardNumber => "OP05-067";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.LifeCount > 3) return Task.CompletedTask;

        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        return Task.CompletedTask;
    }
}
