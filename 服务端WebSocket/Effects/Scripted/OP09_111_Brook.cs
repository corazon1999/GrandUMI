using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-111 布鲁克（角色 / 光 5 费 6000，艾格赫德/草帽一伙）
/// 【触发】我方领袖拥有《艾格赫德》特征，且对方持有6张或更多手牌的场合，对方丢弃其2张手牌。
///
/// 实现说明：
/// - 【触发】= 生命牌触发 OnLifeRevealTrigger。
/// - DSL 的 trigger 节不支持条件判断，故用脚本：在生命触发时检查
///   「我方领袖含《艾格赫德》特征」且「对方手牌 ≥6」，满足则令对方弃 2 张手牌。
/// - 对方弃牌由对方自选，用 AtomicOps.OpponentDiscardChosen（需 ctx.Engine）。
/// </summary>
public class OP09_111_Brook : IScriptedEffect
{
    public string CardNumber => "OP09-111";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方领袖拥有《艾格赫德》特征
        if (!me.Leader.Info.HasKeyword("艾格赫德")) return;
        // 条件：对方持有 6 张或更多手牌
        if (opp.Hand.Count < 6) return;

        if (ctx.Engine is null) return;
        await AtomicOps.OpponentDiscardChosen(ctx.Engine, 1 - ctx.OwnerIndex, 2);
    }
}
