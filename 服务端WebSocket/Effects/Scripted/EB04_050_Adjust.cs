using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-050 我来好好调教调教你们♡（事件 / 地 / 海军・利刃）
/// 【主要】本回合中，我方最多 1 张拥有《利刃》特征的领袖或角色也可以攻击对方处于活跃状态的角色。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 实现说明：
///   - 【主要】(EventMain)：让玩家选 1 张《利刃》领袖或角色，本回合赋予"可攻击活跃"关键词
///     （Wave3：ActionValidator 允许其攻击对方活跃角色）。
///   - 【反击】(EventCounter)：我方领袖本次战斗力量 +3000（AddPowerThisBattle）。
/// </summary>
public class EB04_050_Adjust : IScriptedEffect
{
    public string CardNumber => "EB04-050";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            var targets = new List<CardInstance>();
            if (me.Leader.Info.HasKeyword("利刃")) targets.Add(me.Leader);
            targets.AddRange(me.Characters.Where(c => c.Info.HasKeyword("利刃")));
            if (targets.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "选择最多 1 张《利刃》领袖或角色，本回合可攻击对方活跃状态的角色",
                targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.GiveKeyword(tgt, "可攻击活跃", KeywordDuration.ThisTurn);
            }
            return;
        }

        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 我方领袖本次战斗力量 +3000
            AtomicOps.AddPowerThisBattle(me.Leader, 3000);
            return;
        }
    }
}
