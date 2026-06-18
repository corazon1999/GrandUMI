using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-033 马赫维斯（角色 / 风 / 堂吉诃德海盗团）
/// 【登场时】我方领袖拥有《堂吉诃德海盗团》特征的场合，将对方最多 1 张费用不高于 5 的角色转为休息状态。
///   之后，当本回合结束时，将我方最多 1 张咚!! 转为活跃状态。
///
/// 实现说明：
///   - 第一段：领袖含特征时，选对方 1 张费用≤5 角色 RestCard。
///   - 第二段：入队 EndTurnTask{Kind="RefreshOwnDon"}，由 TurnEngine.EnterEndPhase 在回合结束时
///     将我方 1 张休息咚转为活跃（已有延迟通道）。
/// </summary>
public class OP04_033_Machvise : IScriptedEffect
{
    public string CardNumber => "OP04-033";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 前置：我方领袖拥有《堂吉诃德海盗团》特征
        if (!me.Leader.Info.HasKeyword("堂吉诃德海盗团")) return;

        // 第一段：对方最多 1 张费用≤5 角色转为休息状态
        var cands = opp.Characters.Where(c => c.CurrentCost() <= 5).ToList();
        if (cands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张费用≤5 的角色转为休息状态",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.RestCard(tgt);
            }
        }

        // 第二段：本回合结束时，将我方最多 1 张咚!! 转为活跃状态
        ctx.State.EndOfTurnTasks.Add(new EndTurnTask { Kind = "RefreshOwnDon", Owner = ctx.OwnerIndex });
    }
}
