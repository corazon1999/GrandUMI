using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-068 汉堡（角色）
/// 【咚!!×1】【攻击时】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，
///   从咚!!卡组中追加最多 1 张休息状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 【咚!!×1】的发动门槛（贴咚数量）由引擎在触发前判定，这里只实现效果本体。
///   - 双方咚!! 张数相对比较用 me.TotalDonInCostArea &lt;= opp.TotalDonInCostArea。
///   - 追加休息咚!! 用 RefreshDonFromDeck(me, 1, DonState.Rest)。
/// </summary>
public class OP07_068_Hamburg : IScriptedEffect
{
    public string CardNumber => "OP07-068";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 【咚!!×1】：本卡需被赋予咚≥1才发动（引擎不预检攻击时咚门槛，须脚本自检）
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return Task.CompletedTask;

        if (me.TotalDonInCostArea > opp.TotalDonInCostArea) return Task.CompletedTask;

        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        return Task.CompletedTask;
    }
}
