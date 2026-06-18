using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-068 托雷波尔（角色 / 暗 5 费 5000）
/// 【对方的回合中】【每回合1次】当我方场上的咚!!放回咚!!卡组时，我方领袖拥有《堂吉诃德海盗团》
///   特征的场合，从咚!!卡组中追加最多1张休息状态的咚!!。
///
/// 实现说明：
///   - 使用反应式 watcher 触发 OnDonReturnedToDeck（咚!!放回咚!!卡组时派发，payload: ctx.Vars["count"]）。
///   - 仅【对方的回合中】生效（CurrentTurnPlayer != OwnerIndex），且每回合1次。
///   - 条件：我方领袖拥有《堂吉诃德海盗团》特征。
///   - 收益：RefreshDonFromDeck(me, 1, Rest)（受咚卡组余量与费用区上限约束）。
/// </summary>
public class OP14_068_Trebol : IScriptedEffect
{
    public string CardNumber => "OP14-068";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public Task Resolve(EffectContext ctx)
    {
        // 仅【对方的回合中】生效
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return Task.CompletedTask;

        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方领袖拥有《堂吉诃德海盗团》特征
        if (me.Leader is null || !me.Leader.Info.HasKeyword("堂吉诃德海盗团")) return Task.CompletedTask;

        // 【每回合1次】
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        // 从咚!!卡组追加最多 1 张休息状态的咚!!
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        return Task.CompletedTask;
    }
}
