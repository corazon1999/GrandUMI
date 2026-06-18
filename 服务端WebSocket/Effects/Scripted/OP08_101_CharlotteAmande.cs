using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-101 夏洛特·安洁尔（角色 / 光 2 费 3000，大妈海盗团）
/// 【启动主要】【每回合1次】可以将我方生命区最上方的 1 张卡牌放置到废弃区：
///   我方领袖拥有《大妈海盗团》特征的场合，当本回合结束时，
///   将我方卡组最上方的 1 张卡牌加入生命区最上方。
///
/// 实现说明：
///   - 触发：ActivatedMain（支付成本） + OnMyTurnEnd（兑现"本回合结束时"延迟收益）。
///   - 每回合 1 次：TurnOnceUsed 标记。
///   - 成本（可选）："将我方生命区最上方 1 张放废弃"。需生命区至少 1 张；用 ConfirmOptional 询问。
///   - 延迟收益仅当领袖含《大妈海盗团》时成立；用本卡 OncePerTurnUsedKeys 标记，
///     我方回合结束时将卡组顶 1 张入生命顶（AddLifeFromDeckTop）。
/// </summary>
public class OP08_101_CharlotteAmande : IScriptedEffect
{
    private const string PendingKey = "OP08-101-PendingLife";

    public string CardNumber => "OP08-101";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.ActivatedMain || t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
            if (me.TurnOnceUsed.Contains(key)) return;

            // 成本前置：生命区至少 1 张
            if (me.LifeArea.Count < 1) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "夏洛特·安洁尔【启动主要】：将我方生命区最上方 1 张放废弃，本回合结束时卡组顶 1 张入生命？");
            if (!use) return;

            me.TurnOnceUsed.Add(key);

            // 成本：生命区最上方 1 张放废弃
            var top = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            me.Trash.Add(top);

            // 仅当领袖含《大妈海盗团》时标记延迟收益
            if (me.Leader.Info.HasKeyword("大妈海盗团"))
                ctx.Source.OncePerTurnUsedKeys.Add(PendingKey);
            return;
        }

        // OnMyTurnEnd：本回合结束时卡组顶 1 张加入生命区最上方
        if (!ctx.Source.OncePerTurnUsedKeys.Contains(PendingKey)) return;
        ctx.Source.OncePerTurnUsedKeys.Remove(PendingKey);
        AtomicOps.AddLifeFromDeckTop(me, 1);
    }
}
