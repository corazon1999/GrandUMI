using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-118 莉莉丝（角色 / 光）
/// 【登场时】我方领袖拥有《艾格赫德》特征的场合，将我方手牌中最多 1 张
///   费用不高于 5 且拥有《艾格赫德》特征或【触发】效果的角色卡牌登场。
///
/// 实现说明：
///   - 前提：领袖含《艾格赫德》(HasKeyword)。
///   - 候选：手牌角色，费用≤5，且 (HasKeyword("艾格赫德") || Trigger 非空)。
///   - 玩家选 0..1 张，PlayFromHandFree 登场。
/// </summary>
public class P_118_Lilith : IScriptedEffect
{
    public string CardNumber => "P-118";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (!me.Leader.Info.HasKeyword("艾格赫德")) return;

        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 5 &&
            (c.Info.HasKeyword("艾格赫德") || !string.IsNullOrEmpty(c.Info.Trigger))
        ).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场最多 1 张费用≤5 且拥有《艾格赫德》或【触发】的角色",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
