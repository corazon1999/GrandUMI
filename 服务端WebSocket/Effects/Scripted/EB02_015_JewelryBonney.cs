using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-015 杰丽·邦妮（角色 / 风 / 超新星・邦妮海盗团，cost7 power7000）
/// 【登场时】对方最多 1 张处于休息状态的角色，在下个对方的重置阶段中不会转为活跃状态。
///   之后，当本回合结束时，将我方最多 1 张咚!! 转为活跃状态。
///
/// 实现说明：
///   - 第一段：选对方 1 张休息角色 → AtomicOps.PreventActivateNextReset（下个重置不活跃，已有机制）。
///   - 第二段（M3）：入队 EndTurnTask{Kind="RefreshOwnDon"}，由 TurnEngine.EnterEndPhase 在回合结束时
///     将我方 1 张休息咚转为活跃。
/// </summary>
public class EB02_015_JewelryBonney : IScriptedEffect
{
    public string CardNumber => "EB02-015";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 第一段：对方最多 1 张休息角色，下个对方重置阶段不活跃
        var cands = opp.Characters.Where(c => c.IsTapped).ToList();
        if (cands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCharacter",
                "选择对方最多 1 张休息角色，使其在下个对方重置阶段中不会转为活跃",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.PreventActivateNextReset(tgt);
            }
        }

        // 第二段：本回合结束时，将我方最多 1 张咚!! 转为活跃
        ctx.State.EndOfTurnTasks.Add(new EndTurnTask { Kind = "RefreshOwnDon", Owner = ctx.OwnerIndex });
    }
}
