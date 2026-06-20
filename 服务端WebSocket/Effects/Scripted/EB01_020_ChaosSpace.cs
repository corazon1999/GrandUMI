using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-020 混乱空间（事件 / 风 / 超新星·心脏海盗团）
/// 【主要】我方领袖拥有《超新星》特征的场合，将我方1张角色放回其持有者的手牌，
///   并将我方手牌中最多1张与放回的角色颜色不同且费用不高于2的角色卡牌登场。
///
/// 实现说明：
///   - 条件：领袖含《超新星》。
///   - "颜色不同"判定为：候选角色的 ColorList 与被放回角色的 ColorList 无任何共同颜色。
///   - 先选我方1张角色放回手牌，再从手牌中选最多1张满足条件的角色登场。
/// </summary>
public class EB01_020_ChaosSpace : IScriptedEffect
{
    public string CardNumber => "EB01-020";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (!me.Leader.Info.HasKeyword("超新星")) return;

        var chars = me.Characters.ToList();
        if (chars.Count == 0) return;

        // 将我方1张角色放回手牌
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方1张角色放回手牌",
            chars.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count == 0) return;
        var bounced = chars.First(c => c.Id.ToString() == pick[0]);
        var bouncedColors = bounced.Info.ColorList;
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, bounced);

        // 从手牌中选最多1张与放回角色颜色不同且费用≤2的角色登场
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 2 &&
            !c.Info.ColorList.Any(col => bouncedColors.Contains(col))
        ).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场手牌中最多1张与放回角色颜色不同且费用≤2的角色",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
