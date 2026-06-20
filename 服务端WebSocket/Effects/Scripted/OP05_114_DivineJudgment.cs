using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-114 神之制裁（事件）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +2000。之后，对方生命卡牌不多于 2 张的场合，
///   本次战斗中，该卡牌的力量再 +2000。
/// 【触发】将对方最多 1 张费用不高于对方生命卡牌张数的角色 KO。
///
/// 说明：
///   - 【反击】对同一选定目标做"之后，对方生命≤2 时再 +2000"的条件追加：脚本中保留 chosen 目标
///     变量，先 +2000，再按 opp.LifeCount<=2 对同一目标 +2000，两次均为本次战斗。
///   - 【触发】动态阈值"费用 ≤ 对方生命卡牌张数"直接用 opp.LifeCount，候选费用用 c.CurrentCost()。
/// </summary>
public class OP05_114_DivineJudgment : IScriptedEffect
{
    public string CardNumber => "OP05-114";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 本次战斗中，我方最多 1 张领袖或角色力量 +2000
            var targets = new List<CardInstance> { me.Leader };
            targets.AddRange(me.Characters);
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "本次战斗中，选择我方最多 1 张领袖或角色 +2000",
                targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count == 0) return;
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 2000);

            // 之后：对方生命卡牌 ≤2 的场合，该卡牌本次战斗中再 +2000
            if (opp.LifeCount <= 2)
                AtomicOps.AddPowerThisBattle(tgt, 2000);
            return;
        }

        // 【触发】将对方最多 1 张费用 ≤ 对方生命卡牌张数的角色 KO
        int threshold = opp.LifeCount;
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= threshold).ToList();
        if (cands.Count == 0) return;
        var koPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            $"将对方最多 1 张费用≤{threshold} 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (koPick.Count > 0)
        {
            var koTgt = cands.First(c => c.Id.ToString() == koPick[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, koTgt);
        }
    }
}
