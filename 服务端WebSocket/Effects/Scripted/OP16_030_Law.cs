using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-030 特拉法尔加·罗（角色，风）
/// 【登场时】对方最多 1 张处于休息状态的角色，在下个对方的重置阶段中不会转为活跃状态。
/// 【我方的回合结束时】将我方所有费用不高于 5 的绿色（风）角色转为活跃状态。
///
/// 实现说明：
///   - 第一段用 PreventActivateNextReset 标记对方休息角色，下个对方重置阶段不会活跃。
///   - 第二段在 OnMyTurnEnd 时机，对我方所有费用≤5 的风色角色循环 ActivateCard。
/// </summary>
public class OP16_030_Law : IScriptedEffect
{
    public string CardNumber => "OP16-030";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 对方最多 1 张休息状态角色，下个对方重置阶段不会转活跃
            var candidates = opp.Characters.Where(c => c.IsTapped).ToList();
            if (candidates.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCharacter",
                "选择对方最多 1 张休息状态的角色，使其在下个对方重置阶段不会转为活跃",
                candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count == 0) return;

            var target = candidates.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PreventActivateNextReset(target);
            return;
        }

        // ── 【我方的回合结束时】 ──
        if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            var targets = me.Characters
                .Where(c => c.Info.Cost <= 5 && c.Info.ColorList.Contains("绿"))
                .ToList();
            foreach (var c in targets) AtomicOps.ActivateCard(c);
        }
    }
}
