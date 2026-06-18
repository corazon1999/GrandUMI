using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-060 线蚯蚓（角色）
/// 【启动主要】【每回合1次】我方领袖拥有《福克斯海盗团》特征，
///   且我方场上不存在其他“线蚯蚓”的场合，从咚!!卡组中追加最多 1 张休息状态的咚!!。
///
/// 实现说明：
///   - 条件 1：领袖含《福克斯海盗团》特征。
///   - 条件 2：场上不存在其他“线蚯蚓”（排除自身实例）。
///   - 收益：RefreshDonFromDeck(1, Rest)。
/// </summary>
public class OP07_060_Itomimizu : IScriptedEffect
{
    public string CardNumber => "OP07-060";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合 1 次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;

        // 条件：领袖含《福克斯海盗团》特征
        if (!me.Leader.Info.HasKeyword("福克斯海盗团")) return Task.CompletedTask;

        // 条件：场上不存在其他“线蚯蚓”
        bool otherExists = me.Characters.Any(c => c.Id != self.Id && c.MatchesName("线蚯蚓"));
        if (otherExists) return Task.CompletedTask;

        me.TurnOnceUsed.Add(key);
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        return Task.CompletedTask;
    }
}
