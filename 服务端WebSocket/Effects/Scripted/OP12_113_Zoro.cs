using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-113 罗罗诺亚·佐罗（5 费 6000）
/// 【KO时】我方领袖拥有《超新星》特征的场合，将我方手牌中最多 1 张
///   费用不高于 4 且拥有《超新星》特征的角色卡牌以休息状态登场。
///
/// 实现：KO 时检查领袖是否含《超新星》；若是，从手牌筛出
///   费用≤4 且含《超新星》特征的角色，玩家最多选 1 张以休息状态登场。
///   可选效果（min=0）。客户端通过 extra.choiceCards 显示手牌卡面。
/// </summary>
public class OP12_113_Zoro : IScriptedEffect
{
    public string CardNumber => "OP12-113";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方领袖拥有《超新星》特征
        if (!me.Leader.Info.HasKeyword("超新星")) return;

        // 候选：手牌中费用≤4 且含《超新星》特征的角色
        var candidates = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character
                     && c.Info.Cost <= 4
                     && c.Info.HasKeyword("超新星"))
            .ToList();
        if (candidates.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "选最多 1 张费用≤4 且含《超新星》特征的角色，以休息状态登场",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is null) return;

        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, target);
        target.IsTapped = true; // 以休息状态登场
    }
}
