using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-001 克里克（领航）
/// 【咚!!×1】【对方的回合中】我方场上的角色仅为拥有《东海》特征的角色的场合，对方所有角色力量-2000。
/// 【启动主要】【每回合1次】将对方最多1张被赋予2张或更多咚!!的角色转为休息状态。
///
/// 永续部分（条件检查）通过 StateSnapshotBuilder 在 powerCurrent 计算时间接体现；
/// 这里实现启动主要的 1 次激活。
/// </summary>
public class OP15_001_Krieg : IScriptedEffect
{
    public string CardNumber => "OP15-001";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];

        // 每回合 1 次锁
        var key = "OP15-001-MainOncePerTurn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 赋予条件：自身被赋予 >= 1 张咚
        if (me.AttachedDonCount(me.Leader.Id) < 1) return;

        // 候选目标：对方被赋予 >= 2 张咚的角色
        var candidates = opp.Characters
            .Where(c => opp.AttachedDonCount(c.Id) >= 2)
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(
            ctx.OwnerIndex, "OpponentCharacterWithDonGe2",
            "选择对方最多 1 张被赋予 2 张或更多咚的角色转为休息状态",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);

        if (chosen.Count == 0) return;
        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.RestCard(target);
        me.TurnOnceUsed.Add(key);
    }
}
