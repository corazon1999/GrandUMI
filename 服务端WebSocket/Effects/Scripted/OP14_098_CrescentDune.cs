using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-098 弯月形沙丘（事件 / 地 / 1 费 / 因佩尔地狱·原巴洛克工作室）
/// 【主要】场上存在费用为 0 或费用为 8 或更高的角色的场合，直到下个对方的结束阶段结束时为止，
///   我方所有拥有的特征中包含〈巴洛克工作室〉的角色费用 +3。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 实现说明 / 简化点：
///   - 多时机：EventMain / EventCounter，并兼容 OnLifeRevealTrigger(生命触发以反击收益结算)。
///   - 【主要】场上条件按原本费用 Info.Cost 判定，"场上"含双方领袖与角色。
///     〈巴洛克工作室〉匹配用 HasKeyword(含"原巴洛克工作室"等子串特征)。
///   - 费用 +3 对我方每张符合角色逐张 AddCostModifier(UntilNextOpponentEndPhase)。
/// </summary>
public class OP14_098_CrescentDune : IScriptedEffect
{
    public string CardNumber => "OP14-098";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain
        || t == EffectTrigger.EventCounter
        || t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            // 场上(双方领袖+角色)存在费用为 0 或费用≥8 的角色
            var allChars = me.Characters.Concat(opp.Characters).ToList();
            bool cond = allChars.Any(c => c.Info.Cost == 0 || c.Info.Cost >= 8);
            if (!cond) return Task.CompletedTask;

            foreach (var c in me.Characters.Where(c => c.Info.HasKeyword("巴洛克工作室")))
            {
                AtomicOps.AddCostModifier(c, 3, KeywordDuration.UntilNextOpponentEndPhase);
            }
            return Task.CompletedTask;
        }

        // 【反击】本次战斗中，我方领袖力量 +3000
        AtomicOps.AddPowerThisBattle(me.Leader, 3000);
        return Task.CompletedTask;
    }
}
