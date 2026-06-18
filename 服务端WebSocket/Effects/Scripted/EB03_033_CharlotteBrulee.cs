using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-033 夏洛特·布蕾（角色 / 暗 / 大妈海盗团）
/// 【对方的回合中】【每回合1次】当我方场上的咚!!因我方的效果被放回咚!!卡组时，
///   我方领袖拥有《大妈海盗团》特征的场合，从咚!!卡组中追加最多 1 张休息状态的咚!!。
///
/// 实现说明：
///   - 用 Wave2 反应式 watcher OnDonReturnedToDeck 监听"咚!!放回咚!!卡组"。
///   - 仅【对方的回合中】生效；【每回合1次】；且需领袖含《大妈海盗团》。
/// </summary>
public class EB03_033_CharlotteBrulee : IScriptedEffect
{
    public string CardNumber => "EB03-033";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【对方的回合中】
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return Task.CompletedTask;

        // 领袖含《大妈海盗团》
        if (!me.Leader.Info.HasKeyword("大妈海盗团")) return Task.CompletedTask;

        // 【每回合1次】
        var key = CardNumber + "-donreturn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        // 从咚!!卡组追加最多 1 张休息状态的咚!!
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        return Task.CompletedTask;
    }
}
