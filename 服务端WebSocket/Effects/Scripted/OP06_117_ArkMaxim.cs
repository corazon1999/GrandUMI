using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-117 方舟箴言（舞台）
/// 【启动主要】【每回合1次】可以将此卡牌和我方的 1 张"艾尼路"转为休息状态：
///   将对方所有费用不高于 2 的角色 KO。
///
/// 实现说明 / 简化点：
///   - 发动成本为"将此舞台 + 我方 1 张'艾尼路'同时转为休息状态"。该指定名称角色横置成本不在 DSL
///     cost 集合内，故用脚本实现：要求我方场上存在活跃（未横置）的"艾尼路"角色作为成本。
///   - 无可横置的"艾尼路"或此舞台已横置时不发动。
/// </summary>
public class OP06_117_ArkMaxim : IScriptedEffect
{
    public string CardNumber => "OP06-117";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合 1 次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本前提：此舞台需为活跃，且存在 1 张活跃的"艾尼路"领袖或角色
        if (self.IsTapped) return;
        var enels = new[] { me.Leader }.Concat(me.Characters)
            .Where(c => !c.IsTapped && c.MatchesName("艾尼路") && AtomicOps.CanRestCard(ctx.State, c))
            .ToList();
        if (enels.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "方舟箴言【启动主要】：将此舞台和我方 1 张活跃的“艾尼路”领袖或角色转为休息，KO 对方所有费用≤2 的角色？");
        if (!use) return;

        // 成本：选择 1 张活跃"艾尼路"横置
        var enelPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnEnel",
            "选择 1 张活跃的“艾尼路”领袖或角色转为休息状态（成本）",
            enels.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (enelPick.Count < 1) return; // 未支付成本

        var enel = enels.First(c => c.Id.ToString() == enelPick[0]);
        if (!AtomicOps.CanRestCard(ctx.State, enel) || !AtomicOps.CanRestCard(ctx.State, self)) return;
        if (!AtomicOps.RestCard(enel) || !AtomicOps.RestCard(self)) return;

        me.TurnOnceUsed.Add(key);

        // 效果：将对方所有费用不高于 2 的角色同时 KO。
        // 必须走可交互的效果 KO 流程，让 OP17-095 等离场置换能够观察整批目标，
        // 并确保一次支付可按卡文保护同一批次内的全部角色。
        var targets = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 2).ToList();
        await AtomicOps.KOCardsByEffectAsync(
            ctx.State,
            1 - ctx.OwnerIndex,
            targets,
            ctx.Prompts,
            ctx.OwnerIndex);
    }
}
