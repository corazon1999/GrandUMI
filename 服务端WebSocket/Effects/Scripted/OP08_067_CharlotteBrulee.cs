using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-067 夏洛特·布玲（角色）
/// 【我方的回合中】【每回合1次】当我方场上的咚!!放回咚!!卡组时，
///   从咚!!卡组中追加最多1张休息状态的咚!!。
///
/// 实现说明：
///   - 用 Wave2 反应式 watcher OnDonReturnedToDeck 监听"咚!!放回咚!!卡组"。
///   - payload 仅含 count；仅在我方回合视为我方咚放回，且【每回合1次】。
/// </summary>
public class OP08_067_CharlotteBrulee : IScriptedEffect
{
    public string CardNumber => "OP08-067";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【我方的回合中】
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;

        // 【每回合1次】
        var key = CardNumber + "-donreturn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        // 从咚!!卡组追加最多 1 张休息状态的咚!!
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        return Task.CompletedTask;
    }
}
