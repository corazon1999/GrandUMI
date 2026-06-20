using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-017 洛克（角色）
/// 【登场时】我方场上不存在"斯考奇"的场合，将我方手牌中最多 1 张"斯考奇"登场。
///
/// 实现说明：
///   - "我方场上不存在斯考奇"：检查我方角色区是否有名称含"斯考奇"的角色，
///     有则不发动（条件不满足）。
///   - 否则从手牌中选择最多 1 张名称含"斯考奇"的角色登场（手牌为我方公开区，仍传 choiceCards 以展示卡面）。
/// </summary>
public class OP10_017_Rock : IScriptedEffect
{
    public string CardNumber => "OP10-017";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方场上不存在"斯考奇"
        bool exists = me.Characters.Any(c => c.MatchesName("斯考奇"));
        if (exists) return;

        var candidates = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character && c.MatchesName("斯考奇"))
            .ToList();
        if (candidates.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多 1 张\"斯考奇\"登场",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = candidates.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
