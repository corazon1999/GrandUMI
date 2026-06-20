using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-028 橡皮橡皮冠军回转弹（事件 / 水 / 因佩尔地狱·草帽一伙）
/// 【反击】我方领袖拥有《因佩尔地狱》特征的场合，本次战斗中，我方最多 1 张领袖或角色力量 +2000。
///   之后，对方将其 1 张处于活跃状态的角色放回其持有者的手牌。
///
/// 实现说明：
///   - 【反击】= EventCounter 时机。
///   - 力量 +2000 用 AddPowerThisBattle（本次战斗中）。
///   - "对方将其 1 张活跃角色放回手牌"由对方决策：用对方 prompt（1 - OwnerIndex）让对方在自己活跃角色中选 1 张退回。
/// </summary>
public class EB01_028_GomuGomuChampionTwister : IScriptedEffect
{
    public string CardNumber => "EB01-028";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (!me.Leader.Info.HasKeyword("因佩尔地狱")) return;

        // 本次战斗中，我方最多 1 张领袖或角色 +2000
        var buffTargets = new List<CardInstance> { me.Leader };
        buffTargets.AddRange(me.Characters);
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，我方最多 1 张领袖或角色力量 +2000",
            buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = buffTargets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisBattle(tgt, 2000);
        }

        // 之后：对方将其 1 张活跃角色放回手牌（由对方选择）
        var oppActive = opp.Characters.Where(c => !c.IsTapped).ToList();
        if (oppActive.Count == 0) return;

        var oppPick = await ctx.Prompts.ChooseCards(1 - ctx.OwnerIndex, "OwnCharacter",
            "将你 1 张处于活跃状态的角色放回手牌",
            oppActive.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (oppPick.Count > 0)
        {
            var bounce = oppActive.First(c => c.Id.ToString() == oppPick[0]);
            AtomicOps.BounceToHand(ctx.State, 1 - ctx.OwnerIndex, bounce);
        }
    }
}
