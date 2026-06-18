using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-001 艾斯（领航）
/// 启动主要每回合 1 次：本回合中，我方 1 张力量 ≥8000 的"路飞"或力量 ≥8000 的《白胡子海盗团》获得【速攻】
/// </summary>
public class OP16_001_Ace : IScriptedEffect
{
    public string CardNumber => "OP16-001";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = "OP16-001-MainOncePerTurn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 原文「我方 1 张力量≥8000 的…」含领袖自身（艾斯 keyWords 含「白胡子海盗团」），故候选池纳入领袖
        var pool = new List<CardInstance> { me.Leader };
        pool.AddRange(me.Characters);
        var candidates = pool.Where(c =>
            c.Info.Power >= 8000 &&
            (c.MatchesName("蒙奇·D·路飞") || c.Info.HasKeyword("白胡子海盗团"))
        ).ToList();
        if (candidates.Count == 0) return;

        var picked = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharForRush",
            "选择 1 张获得【速攻】的角色（力量 ≥8000）",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (picked.Count == 0) return;

        var tgt = candidates.First(c => c.Id.ToString() == picked[0]);
        AtomicOps.GiveKeyword(tgt, "速攻", KeywordDuration.ThisTurn);
        me.TurnOnceUsed.Add(key);
    }
}
