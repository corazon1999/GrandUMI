using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-073 特拉法尔加·罗（暗 / 心脏海盗团）
/// 【登场时】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，从咚!!卡组中追加最多 1 张活跃状态的咚!!。
///   之后，直到下个对方的结束阶段结束时为止，我方“堂吉诃德·罗西南德”和所有拥有《心脏海盗团》特征的角色力量 +1000。
///
/// 说明 / 简化点：
///   - 第 1 段为条件型强制效果：满足"我方咚 ≤ 对方咚"时从咚卡组追加 1 张活跃咚（不足则尽量追加）。
///   - 第 2 段为无条件持续 buff：目标为我方场上名为"堂吉诃德·罗西南德"的角色，
///     以及所有拥有《心脏海盗团》特征的角色（领袖不在文本范围内，仅角色）。
///     "直到下个对方结束阶段" 用 PowerModPersistent 表达（由引擎在下个对方结束阶段清理）。
/// </summary>
public class OP12_073_Law : IScriptedEffect
{
    public string CardNumber => "OP12-073";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 1. 我方咚 ≤ 对方咚 → 从咚卡组追加最多 1 张活跃咚
        if (me.TotalDonInCostArea <= opp.TotalDonInCostArea)
        {
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        }

        // 2. 我方"堂吉诃德·罗西南德"和所有《心脏海盗团》角色力量 +1000（持续到下个对方结束阶段）
        foreach (var c in me.Characters)
        {
            if (c.MatchesName("堂吉诃德·罗西南德") || c.Info.HasKeyword("心脏海盗团"))
            {
                AtomicOps.AddPowerPersistent(c, 1000);
            }
        }

        return Task.CompletedTask;
    }
}
