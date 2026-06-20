using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-093 霍明格圣（角色 / 紫(地) / 1 费 / 0 / 天龙人）
/// 【启动主要】可以将此角色放置到废弃区：直到下个对方的回合结束时为止，
///   我方最多 1 张黑色角色费用 +3。
///
/// 说明 / 简化点：
///   - 成本：将此角色放置到废弃区（同步 KOCard，不触发"被 KO 时"效果），与既有"放置到废弃区"范式一致。
///   - "黑色角色" = ColorList 含 "紫"。filter 按颜色筛选用脚本实现。
///   - "直到下个对方的回合结束时为止" → KeywordDuration.UntilNextOpponentEndPhase。
/// </summary>
public class OP10_093_Homing : IScriptedEffect
{
    public string CardNumber => "OP10-093";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 收益候选：我方黑色(暗)角色
        var cands = me.Characters.Where(c => c.Info.ColorList.Contains("紫")).ToList();

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "霍明格圣【启动主要】：将此角色放置废弃区，使我方最多 1 张黑色角色费用 +3（至下个对方回合结束）？");
        if (!use) return;

        // 成本：将自身放置到废弃区（同步，不触发被 KO 时效果）
        AtomicOps.KO(ctx.State, ctx.OwnerIndex, self);

        // 收益：我方最多 1 张黑色角色费用 +3
        if (cands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方最多 1 张黑色角色，费用 +3",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;
        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddCostModifier(tgt, 3, KeywordDuration.UntilNextOpponentEndPhase);
    }
}
