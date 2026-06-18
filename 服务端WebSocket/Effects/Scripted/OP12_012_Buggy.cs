using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-012 巴奇（2 费 2000）
/// 【登场时】直到下个对方的结束阶段结束时为止，我方最多 1 张"巴奇"以外的、
///   拥有的特征中包含〈罗杰海盗团〉的角色获得【阻挡者】效果。
///
/// 实现：从我方场上角色中筛出"非巴奇且含《罗杰海盗团》特征"的候选，
/// 让玩家最多选 1 张赋予【阻挡者】（UntilNextOpponentEndPhase）。"最多 1 张" = min=0（可跳过）。
/// （按名称排除"巴奇"已自动排除此卡自身。）
/// </summary>
public class OP12_012_Buggy : IScriptedEffect
{
    public string CardNumber => "OP12-012";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 候选：我方场上、含《罗杰海盗团》特征、且名称非"巴奇"的角色
        var candidates = me.Characters
            .Where(c => c.Info.HasKeyword("罗杰海盗团") && !c.MatchesName("巴奇"))
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnRogerCharacter",
            "选最多 1 张《罗杰海盗团》角色（巴奇以外）获得【阻挡者】（直到下个对方结束阶段）",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null)
            AtomicOps.GiveKeyword(target, "阻挡者", KeywordDuration.UntilNextOpponentEndPhase);
    }
}
