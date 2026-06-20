using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-058 克洛克达尔（领袖 / 暗·光 / 王下七武海·B・W）
/// 【对方的回合中】【每回合1次】我方场上的咚!!因我方的效果被放回咚!!卡组时，
///   从咚!!卡组中追加最多 1 张活跃状态的咚!!。
///
/// 实现说明：
///   - 用 Wave2 的 OnDonReturnedToDeck watcher（我方咚放回咚卡组时派发）。
///   - 限对方回合中（CurrentTurnPlayer != owner）且每回合 1 次。
///   - 追加 1 张活跃咚：RefreshDonFromDeck(me, 1, Active)。
/// </summary>
public class OP04_058_Crocodile : IScriptedEffect
{
    public string CardNumber => "OP04-058";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅在对方回合中生效
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return Task.CompletedTask;

        // 每回合 1 次
        var key = "OP04-058-don" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        // 从咚!!卡组中追加最多 1 张活跃状态的咚!!
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        return Task.CompletedTask;
    }
}
