using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-065 格罗（3 费 0 力量，阿拉巴斯坦王国/温泉岛）
/// 【登场时】公开我方卡组最上方的 1 张卡牌。公开的卡牌费用不高于 2 的场合，
///   从咚!!卡组中追加最多 1 张休息状态的咚!!。
///
/// 实现说明 / 简化点：
/// - "公开卡组顶 1 张"在结算上不改变卡组顺序（仅展示信息），故直接读取 me.Deck[0] 判定费用，
///   并通过公共公开事件向双方展示。
/// - 费用判定取卡面原始费用 c.Info.Cost。
/// - 满足条件时 RefreshDonFromDeck(me, 1, Rest)（受咚卡组余量与上限约束）。
/// </summary>
public class OP15_065_Geuro : IScriptedEffect
{
    public string CardNumber => "OP15-065";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Deck.Count == 0) return Task.CompletedTask;

        var top = me.Deck[0];

        // 公开卡组顶 1 张，公开本身不要求玩家确认。
        ctx.BroadcastReveal(top);

        // 费用不高于 2 → 从咚卡组追加最多 1 张休息状态咚
        if (top.Info.Cost <= 2)
        {
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        }
        return Task.CompletedTask;
    }
}
