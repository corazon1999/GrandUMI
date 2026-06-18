using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-074 赫拉（角色，暗）
/// 【登场时】将我方手牌中最多 1 张力量不高于 3000 且拥有〈Homies〉特征的角色卡牌登场。
///
/// 实现说明 / 简化点：
///   - 候选 = 我方手牌中「角色卡、原本力量 ≤3000、特征含〈Homies〉」者。
///   - “最多 1 张”→ min=0、max=1（玩家可不登场）。
///   - 登场为免费（PlayFromHandFree）。
///   - 手牌身份对客户端默认不可见，故传 extra.choiceCards 显示卡面。
/// </summary>
public class OP13_074_Hera : IScriptedEffect
{
    public string CardNumber => "OP13-074";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 候选：手牌中力量≤3000 且含〈Homies〉特征的角色卡
        var candidates = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character
                     && c.Info.Power <= 3000
                     && c.Info.HasKeyword("Homies"))
            .ToList();
        if (candidates.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多 1 张力量不高于 3000 且拥有〈Homies〉特征的角色登场",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, target);
    }
}
