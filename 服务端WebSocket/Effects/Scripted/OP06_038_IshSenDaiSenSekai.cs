using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-038 一大・三千・大千・世界（事件，风，德莱斯罗兹/草帽一伙）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +2000。之后，我方场上存在 8 张或更多
///         处于休息状态的卡牌的场合，本次战斗中，该卡牌的力量再 +2000。
///
/// 实现说明 / 简化点：
///   - 选择 1 张我方领袖或角色，本战斗 +2000；wfReason 认为 counter 节不支持 if，但 C# 可直接判断。
///   - "处于休息状态的卡牌" 统计我方领袖/角色/舞台中 IsTapped 者，以及成本区中休息状态的咚!!。
///   - 满足 ≥8 张时，对同一张目标再 +2000（同样本战斗）。
///   - 【触发】节将对方≤1张休息且费用≤3角色KO由触发机制单独处理，此处仅实现【反击】。
/// </summary>
public class OP06_038_IshSenDaiSenSekai : IScriptedEffect
{
    public string CardNumber => "OP06-038";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var targets = new List<CardInstance>();
        if (me.Leader is not null) targets.Add(me.Leader);
        targets.AddRange(me.Characters);
        if (targets.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张我方领袖或角色，本次战斗力量 +2000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = targets.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddPowerThisBattle(target, 2000);

        // 统计我方场上处于休息状态的卡牌数量（领袖/角色/舞台 + 休息咚!!）
        int restedCount = 0;
        if (me.Leader is not null && me.Leader.IsTapped) restedCount++;
        restedCount += me.Characters.Count(c => c.IsTapped);
        if (me.StageCard is not null && me.StageCard.IsTapped) restedCount++;
        restedCount += me.CostArea.Count(d => d.State == DonState.Rest);

        if (restedCount >= 8)
        {
            AtomicOps.AddPowerThisBattle(target, 2000);
        }
    }
}
