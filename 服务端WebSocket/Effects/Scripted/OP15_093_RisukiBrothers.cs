using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-093 里斯奇兄弟（角色）
/// 【启动主要】可以将此角色放置到废弃区：我方废弃区中有 15 张或更多卡牌的场合，
///   本回合中，我方最多 1 张角色"蒙奇·D·路飞"获得【速攻：角色】效果和属性（斩）。
///
/// 实现说明 / 简化点：
///   - 成本：将此角色放置到废弃区（先支付）。"可以"=可选，先 ConfirmOptional。
///   - 收益条件：我方废弃区 ≥15 张（含放入的此卡）。
///   - "获得【速攻：角色】"用 GiveKeyword(target, "速攻", ThisTurn) 表达（引擎据此本回合放行登场回合攻击）。
///     "属性（斩）"为属性追加，引擎无属性临时修正通道，故未实现该副效果（仅影响极少数属性联动）。
/// </summary>
public class OP15_093_RisukiBrothers : IScriptedEffect
{
    public string CardNumber => "OP15-093";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 目标：我方角色中名为"蒙奇·D·路飞"者
        var targets = me.Characters
            .Where(c => c.Id != self.Id && c.Info.Name.Contains("蒙奇·D·路飞"))
            .ToList();
        if (targets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "里斯奇兄弟【启动主要】：将此角色放置到废弃区（需废弃区≥15张），令我方1张“蒙奇·D·路飞”本回合获得【速攻】？");
        if (!use) return;

        // 成本：将此角色放置到废弃区
        me.Characters.Remove(self);
        me.Trash.Add(self);
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == self.Id.ToString());

        // 收益条件：废弃区 ≥15 张（此卡已加入废弃区）
        if (me.Trash.Count < 15) return;

        // 目标可能因放入废弃区集合变动，重新取一次
        var liveTargets = me.Characters
            .Where(c => c.Info.Name.Contains("蒙奇·D·路飞"))
            .ToList();
        if (liveTargets.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = liveTargets.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方最多 1 张“蒙奇·D·路飞”，本回合获得【速攻】",
            liveTargets.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (pick.Count == 0) return;

        var tgt = liveTargets.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.GiveKeyword(tgt, "速攻", KeywordDuration.ThisTurn);
    }
}
