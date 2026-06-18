using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-066 希尔巴兹·雷利（暗 8 费 9000，罗杰海盗团）
/// 【速攻】（印刷关键字，由卡牌数据/引擎处理，本脚本不涉及）
/// 【登场时】我方场上存在被赋予中的咚!!的场合，将对方最多 1 张费用不高于 5 的角色转为休息状态。
///   之后，当本回合结束时，从咚!!卡组中追加最多 1 张活跃状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 整个【登场时】以"我方场上存在被赋予中的咚!!（DonState.Attached ≥1）"为条件门槛。
///   - 第一段：候选为对方场上费用 ≤5 的角色，玩家最多选 1 张转休息（min=0 可跳过）。
///   - 第二段"当本回合结束时追加 1 张活跃咚"：引擎暂无"延迟到回合结束"调度机制，
///     故改为在【我方的回合结束时】触发器中补做。为只在登场当回合生效，记录登场时的
///     回合编号（ctx.Source.TurnPlayed），并在回合结束时比对当前 TurnCount；
///     同时要求条件（被赋予咚存在）在登场时成立才安排此追加。
///   - 用 me.TurnOnceUsed 存一个本卡专属标记，登场时写入、回合结束时读取后清掉，
///     确保只追加一次且仅限当回合。
/// </summary>
public class OP13_066_Rayleigh : IScriptedEffect
{
    public string CardNumber => "OP13-066";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnMyTurnEnd;

    private const string PendingKey = "OP13-066-EOT";

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 条件门槛：我方场上存在被赋予中的咚!!
            bool hasAttachedDon = me.CostArea.Any(d => d.State == DonState.Attached);
            if (!hasAttachedDon) return;

            // 第一段：对方最多 1 张费用 ≤5 的角色转休息
            int oppIdx = 1 - ctx.OwnerIndex;
            var opp = ctx.State.Players[oppIdx];
            var candidates = opp.Characters.Where(c => c.Info.Cost <= 5).ToList();
            if (candidates.Count > 0)
            {
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterCostLe5",
                    "将对方最多 1 张费用不高于 5 的角色转为休息状态",
                    candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
                if (chosen.Count > 0)
                {
                    var target = candidates.First(c => c.Id.ToString() == chosen[0]);
                    AtomicOps.RestCard(target);
                }
            }

            // 第二段：在本卡上标记延迟效果，待【我方的回合结束时】结算
            ctx.Source.OncePerTurnUsedKeys.Add(PendingKey);
            return;
        }

        // 【我方的回合结束时】：若本回合曾安排过追加，则执行并清除标记
        if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            if (!ctx.Source.OncePerTurnUsedKeys.Contains(PendingKey)) return;
            ctx.Source.OncePerTurnUsedKeys.Remove(PendingKey);
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        }
    }
}
