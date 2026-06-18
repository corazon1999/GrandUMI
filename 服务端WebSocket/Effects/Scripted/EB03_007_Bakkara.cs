using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-007 芭卡拉（角色 / 炎 / FILM・大德索罗号）
/// 【阻挡者】（关键词，引擎处理）
/// 【KO时】将我方手牌中最多1张力量不高于6000且原本没有效果的角色卡牌登场。
///
/// 实现说明：「原本没有效果」= EffectTags 与 Abilities 均为空。强制效果（非可选）。
/// </summary>
public class EB03_007_Bakkara : IScriptedEffect
{
    public string CardNumber => "EB03-007";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

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
