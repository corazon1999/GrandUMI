using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-003 乌塔（角色 / 炎 / FILM）
/// 【登场时】我方领袖为“乌塔”的场合，抽取2张卡牌。
///   之后，将我方手牌中最多1张力量不高于6000且原本没有效果的角色卡牌登场。
///
/// 实现说明：
///   - 「原本没有效果」= 卡面无任何触发(EffectTags 为空)且无关键词能力(Abilities 为空)。
///   - 登场条件：领袖名含“乌塔”才抽2；之后无论如何均可尝试登场（按文本「之后」紧接）。
/// </summary>
public class EB03_003_Uta : IScriptedEffect
{
    public string CardNumber => "EB03-003";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (!me.Leader.Info.NameContains("乌塔")) return;

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        // 之后：手牌中最多1张力量≤6000且原本没有效果的角色登场
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
