using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-116 裂焰斩（事件）
/// 【主要】/【反击】本回合中，我方最多 1 张领袖或角色力量 +1000。
///   之后，对方生命卡牌不多于 2 张的场合，将对方最多 1 张费用不高于 4 的角色转为休息状态。
///
/// 说明 / 简化点：
///   - "之后，对方生命≤2 时再休置对方角色"是带独立条件的连锁分句：在 C# 中按顺序执行，
///     先做无条件的 +1000，再用 opp.LifeCount<=2 门控后半段休置，与文本结果一致。
///   - 力量增益的持续时间为"本回合中"，用 AddPowerThisTurn。
/// </summary>
public class OP07_116_RetsuenZan : IScriptedEffect
{
    public string CardNumber => "OP07-116";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 本回合中，我方最多 1 张领袖或角色力量 +1000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本回合中，选择我方最多 1 张领袖或角色 +1000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, 1000);
        }

        // 之后：对方生命卡牌 ≤2 的场合，将对方最多 1 张费用 ≤4 的角色转为休息状态
        if (opp.LifeCount > 2) return;
        var restCands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 4).ToList();
        if (restCands.Count == 0) return;
        var restPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤4 的角色转为休息状态",
            restCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (restPick.Count > 0)
        {
            var rt = restCands.First(c => c.Id.ToString() == restPick[0]);
            AtomicOps.RestCard(rt);
        }
    }
}
