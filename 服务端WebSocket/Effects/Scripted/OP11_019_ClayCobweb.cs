using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-019 粘土蛛网（事件）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +2000。
///   之后，对方场上存在力量为 6000 或更高的角色的场合，本回合中，我方最多 1 张领袖或角色力量 +1000。
///
/// 实现说明：
///   - 第一段 +2000 为本次战斗修正（AddPowerThisBattle）。
///   - 第二段条件"对方场上存在力量≥6000 的角色"用 ctx.State.CurrentPowerOf 实时评估；
///     满足时再让我方最多 1 张领袖/角色本回合 +1000（AddPowerThisTurn）。
///   - 【触发】分句由生命触发节单独处理，本反击脚本不涉及。
/// </summary>
public class OP11_019_ClayCobweb : IScriptedEffect
{
    public string CardNumber => "OP11-019";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        // 第一段：本次战斗中，我方最多 1 张领袖或角色 +2000
        var buffTargets = new List<CardInstance> { me.Leader };
        buffTargets.AddRange(me.Characters);
        var picked = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，选择最多 1 张领袖或角色力量 +2000",
            buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (picked.Count > 0)
        {
            var tgt = buffTargets.First(c => c.Id.ToString() == picked[0]);
            AtomicOps.AddPowerThisBattle(tgt, 2000);
        }

        // 第二段条件：对方场上存在力量≥6000 的角色
        bool hasBigOpp = opp.Characters.Any(c => ctx.State.CurrentPowerOf(oppIdx, c) >= 6000);
        if (!hasBigOpp) return;

        var buffTargets2 = new List<CardInstance> { me.Leader };
        buffTargets2.AddRange(me.Characters);
        var picked2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本回合中，选择最多 1 张领袖或角色力量 +1000",
            buffTargets2.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (picked2.Count > 0)
        {
            var tgt = buffTargets2.First(c => c.Id.ToString() == picked2[0]);
            AtomicOps.AddPowerThisTurn(tgt, 1000);
        }
    }
}
