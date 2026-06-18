using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-017 甜蜜之击（事件 / 炎 / 阿拉巴斯坦王国・草帽一伙）
/// 【反击】本回合中，对方最多 1 张领袖或角色力量-2000。
///   之后，我方领袖处于活跃状态的场合，本回合中，对方最多 1 张领袖或角色力量-1000。
///
/// 实现说明：
///   - 反击节(EventCounter)：先选对方 1 张领袖/角色 -2000；
///     之后若我方领袖活跃（未横置），再选对方 1 张领袖/角色 -1000。
///   - 用 AddPowerThisTurn 负值实现"本回合中力量-N"。
/// </summary>
public class OP04_017_SweetStrike : IScriptedEffect
{
    public string CardNumber => "OP04-017";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 第一段：对方最多 1 张领袖或角色 -2000
        var targets1 = new List<CardInstance> { opp.Leader };
        targets1.AddRange(opp.Characters);
        var pick1 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "对方最多 1 张领袖或角色力量-2000",
            targets1.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick1.Count > 0)
        {
            var t = targets1.FirstOrDefault(c => c.Id.ToString() == pick1[0]);
            if (t is not null) AtomicOps.AddPowerThisTurn(t, -2000);
        }

        // 第二段：我方领袖活跃时，对方最多 1 张领袖或角色 -1000
        if (me.Leader.IsTapped) return;

        var targets2 = new List<CardInstance> { opp.Leader };
        targets2.AddRange(opp.Characters);
        var pick2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "我方领袖活跃：对方最多 1 张领袖或角色力量-1000",
            targets2.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick2.Count > 0)
        {
            var t = targets2.FirstOrDefault(c => c.Id.ToString() == pick2[0]);
            if (t is not null) AtomicOps.AddPowerThisTurn(t, -1000);
        }
    }
}
