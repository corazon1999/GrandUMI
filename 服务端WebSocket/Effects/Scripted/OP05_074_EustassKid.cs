using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-074 尤斯塔斯·基德（角色）
/// 【阻挡者】（关键词，引擎处理，本脚本只实现主动效果）
/// 【我方的回合中】【每回合1次】当我方场上的咚!!放回咚!!卡组时，
///   从咚!!卡组中追加最多1张活跃状态的咚!!。
///
/// 实现说明：
///   - 使用反应式 watcher OnDonReturnedToDeck（咚!!放回咚!!卡组时派发）。
///   - 仅【我方的回合中】生效，且每回合1次。
///   - "追加最多1张活跃咚"用 RefreshDonFromDeck(me, 1, Active)，受咚卡组余量/费用区上限约束。
/// </summary>
public class OP05_074_EustassKid : IScriptedEffect
{
    public string CardNumber => "OP05-074";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public Task Resolve(EffectContext ctx)
    {
        // 仅【我方的回合中】生效
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;

        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【每回合1次】
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        // 从咚!!卡组追加最多1张活跃状态的咚!!
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        return Task.CompletedTask;
    }
}
