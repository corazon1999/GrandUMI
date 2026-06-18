using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-007 杰克斯（2 费 2000）
/// 【登场时】本回合中，我方最多 1 张"杰克斯"以外的、拥有的特征中包含〈罗杰海盗团〉的角色获得【速攻】效果。
///
/// 实现：从我方场上角色中筛出"非杰克斯且含《罗杰海盗团》特征"的候选，
/// 让玩家最多选 1 张赋予【速攻】（ThisTurn）。可选效果（min=0）。
/// （按名称排除"杰克斯"已自动排除此卡自身。）
/// </summary>
public class OP12_007_Jax : IScriptedEffect
{
    public string CardNumber => "OP12-007";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 候选：我方场上、含《罗杰海盗团》特征、且名称非"杰克斯"的角色
        var candidates = me.Characters
            .Where(c => c.Info.HasKeyword("罗杰海盗团") && !c.MatchesName("杰克斯"))
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnRogerCharacter",
            "选最多 1 张《罗杰海盗团》角色（杰克斯以外）获得【速攻】",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null)
            AtomicOps.GiveKeyword(target, "速攻", KeywordDuration.ThisTurn);
    }
}
