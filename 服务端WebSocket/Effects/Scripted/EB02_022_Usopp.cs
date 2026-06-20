using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-022 撒谎布（角色 / 水 / 东海·草帽一伙，cost4 power5000）
/// 【登场时】我方力量为5000或更高的角色不多于2张的场合，
///   将我方手牌中最多1张力量不高于6000且原本没有效果的角色卡牌登场。
///
/// 实现说明：
///   - "力量为5000或更高的角色"取场上角色卡面原始力量 c.Info.Power >= 5000；
///     "不多于2张"= 数量 <= 2（含本卡自身，登场时本卡已在场，符合官方裁定按当前场况判定）。
///   - "原本没有效果的角色"= 卡面没有任何效果标签/能力（EffectTags 与 Abilities 均为空）。
///   - 从手牌登场用 PlayFromHandFree。候选为非公开/需展示卡面 → 传 choiceCards。
/// </summary>
public class EB02_022_Usopp : IScriptedEffect
{
    public string CardNumber => "EB02-022";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方力量≥5000的角色不多于2张
        int big = me.Characters.Count(c => c.Info.Power >= 5000);
        if (big > 2) return;

        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Power <= 6000 &&
            c.Info.EffectTags.Length == 0 &&
            c.Info.Abilities.Length == 0
        ).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场最多1张力量≤6000且原本没有效果的角色",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
