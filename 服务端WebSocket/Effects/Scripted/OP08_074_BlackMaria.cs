using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-074 黑色玛利亚（角色 / 暗 3 费 2000，百兽海盗团）
/// 【启动主要】【每回合1次】我方场上不存在其他的角色"黑色玛利亚"的场合，
///   从咚!!卡组中追加最多 5 张休息状态的咚!!。之后，当本回合结束时，将我方场上的咚!!
///   放回咚!!卡组，直到我方场上咚!!的张数与对方场上咚!!的张数相同。
///
/// 实现说明：
///   - 触发：ActivatedMain（追加咚）；回合末回收登记为对局级延迟任务，来源离场后仍会兑现。
///   - 发动条件：我方场上不存在"另一张"角色"黑色玛利亚"。
///   - 每回合 1 次：TurnOnceUsed 标记。
///   - 回合结束时按"我方咚数 - 对方咚数"将超出部分放回咚卡组。
///     我方咚数/对方咚数按成本区(CostArea)总张数计。
/// </summary>
public class OP08_074_BlackMaria : IScriptedEffect
{
    private const string PendingKey = "OP08-074-PendingReturn";

    public string CardNumber => "OP08-074";

    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.ActivatedMain or EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            bool scheduled = ctx.Vars.TryGetValue("scheduled", out var value) && value is true;
            if (!ctx.Source.OncePerTurnUsedKeys.Remove(PendingKey) && !scheduled) return;
            var pending = ctx.State.EndOfTurnTasks.FirstOrDefault(task =>
                task.Kind == "ReturnExcessDonToOpponentCount"
                && task.Owner == ctx.OwnerIndex
                && task.SourceCardId == ctx.Source.Id.ToString());
            if (pending is not null) ctx.State.EndOfTurnTasks.Remove(pending);

            int excess = me.CostArea.Count - ctx.State.Players[1 - ctx.OwnerIndex].CostArea.Count;
            if (excess > 0) await AtomicOps.PromptReturnDonToDeck(ctx, excess, optional: false);
            return;
        }

        // 每回合 1 次
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 发动条件：场上不存在其他的角色"黑色玛利亚"
        bool otherExists = me.Characters.Any(c => c.Id != ctx.Source.Id && c.MatchesName("黑色玛利亚"));
        if (otherExists) return;

        me.TurnOnceUsed.Add(key);

        // 从咚!!卡组追加最多 5 张休息状态的咚!!
        AtomicOps.RefreshDonFromDeck(me, 5, DonState.Rest);

        // 延迟效果由对局状态持有，黑色玛利亚之后离场也不影响回合末结算。
        ctx.Source.OncePerTurnUsedKeys.Add(PendingKey);
        ctx.State.EndOfTurnTasks.Add(new EndTurnTask
        {
            Kind = "ReturnExcessDonToOpponentCount",
            Owner = ctx.OwnerIndex,
            SourceCardId = ctx.Source.Id.ToString(),
        });
        await Task.CompletedTask;
    }
}
