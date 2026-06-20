using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-055 请放心攀登使用吧!!路飞前辈!!!（水 3 费 事件，德莱斯罗兹/巴尔托俱乐部）
/// 【主要】选择以下的 1 项。
///   ・抽取 2 张卡牌。
///   ・直到下个对方的结束阶段结束时为止，我方最多 1 张拥有《德莱斯罗兹》特征的角色获得【阻挡者】效果。
///
/// 实现说明：用 ChooseOption 表达"选择以下 1 项"的二选一分支。
///   选项 1：Draw 2。
///   选项 2：从我方含《德莱斯罗兹》特征的角色中选最多 1 张，赋予【阻挡者】
///           （KeywordDuration.UntilNextOpponentEndPhase = 直到下个对方结束阶段）。
///           若无可选目标，仍可选择该项但无收益。
/// </summary>
public class OP15_055_ClimbOnPlease : IScriptedEffect
{
    public string CardNumber => "OP15-055";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "请放心攀登使用吧!!路飞前辈!!!【主要】：选择以下 1 项",
            new[] { "抽取 2 张卡牌", "我方最多 1 张《德莱斯罗兹》角色获得【阻挡者】" });

        if (opt == 0)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
            return;
        }

        // opt == 1：赋予【阻挡者】
        var candidates = me.Characters
            .Where(c => c.Info.HasKeyword("德莱斯罗兹"))
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacterDressrosa",
            "选择我方最多 1 张《德莱斯罗兹》角色获得【阻挡者】",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.GiveKeyword(target, "阻挡者", KeywordDuration.UntilNextOpponentEndPhase);
    }
}
