using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-040 库赞（领航）
/// 当通过我方拥有《海军》特征的卡牌效果从我方手牌中丢弃卡牌时，丢弃几张卡牌，便抽取相同数量的卡牌。
///
/// 实现说明（反馈#182，此前整卡未实现）：
///   - 监听 OnHandDiscarded watcher：AtomicOps.DiscardHand 逐张派发，故"每收到一次事件抽1张"
///     即等价"丢几张抽几张"。
///   - payload 判定三要素（见 EffectRuntime.NotifyHandDiscarded）：
///       owner==我方（丢的是我方手牌）；actingSide==我方 且来源卡含《海军》（"通过我方《海军》卡牌的效果"）；
///       成本与收益阶段的丢弃都属于"通过该《海军》卡牌的效果丢弃"，因此不排除 isCost=true。
/// </summary>
public class OP12_040_Kuzan : IScriptedEffect
{
    public string CardNumber => "OP12-040";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnHandDiscarded;

    public async Task Resolve(EffectContext ctx)
    {
        // 丢弃的须是我方手牌
        if (!ctx.Vars.TryGetValue("owner", out var ov) || ov is not int owner || owner != ctx.OwnerIndex)
            return;
        // 丢弃来源须是我方控制的效果
        if (ctx.Vars.TryGetValue("actingSide", out var asd) && asd is int acting && acting != ctx.OwnerIndex)
            return;
        // 来源卡须拥有《海军》特征
        var srcNum = ctx.Vars.TryGetValue("sourceNumber", out var sn) ? sn as string : null;
        if (string.IsNullOrEmpty(srcNum)) return;
        var srcInfo = CardDatabase.Get(srcNum);
        if (srcInfo is null || !srcInfo.HasKeyword("海军")) return;

        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        return;
    }
}
