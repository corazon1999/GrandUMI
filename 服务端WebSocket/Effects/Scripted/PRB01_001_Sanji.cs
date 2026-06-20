using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// PRB01-001 山智（领航 / 炎 / 草帽一伙）
/// 【启动主要】【每回合1次】本回合中，我方最多 1 张费用不高于 8 且没有【登场时】效果的角色获得【速攻】效果。
///
/// 实现说明：
///   - 【启动主要】= ActivatedMain 时机；【每回合1次】用 TurnOnceUsed 去重。
///   - "没有【登场时】效果"判定：角色卡 EffectTags 不含 "OnEnterField"（与 DSL noEnterFieldEffect 过滤键一致）。
///   - "费用不高于 8"取该角色当前费用 ctx.State.CurrentCostOf。
///   - 本回合临时赋予【速攻】：AtomicOps.GiveKeyword(c, "速攻", KeywordDuration.ThisTurn)。
/// </summary>
public class PRB01_001_Sanji : IScriptedEffect
{
    public string CardNumber => "PRB01-001";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 候选：费用≤8 且没有【登场时】效果的我方角色
        var cands = me.Characters.Where(c =>
            ctx.State.CurrentCostOf(ctx.OwnerIndex, c) <= 8 &&
            System.Array.IndexOf(c.Info.EffectTags, "OnEnterField") < 0
        ).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "山智【启动主要】：本回合让我方最多 1 张费用≤8 且无【登场时】效果的角色获得【速攻】？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多 1 张角色，使其本回合获得【速攻】",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.GiveKeyword(tgt, "速攻", KeywordDuration.ThisTurn);
    }
}
