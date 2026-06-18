using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-008 斯考奇（角色 / 炎 2 费 1000，班克禁区）
/// 【阻挡者】（关键词，由引擎处理）
/// 【登场时】我方场上不存在"洛克"的场合，将我方手牌中最多1张"洛克"登场。
///
/// 实现说明：
/// - 【阻挡者】为纯关键词，引擎自动处理，此处只实现【登场时】主动效果。
/// - 条件：我方场上（领袖+角色）不存在名为"洛克"的卡牌。
/// - 满足则从手牌选最多 1 张名为"洛克"的角色免费登场（手牌私有区，经 extra.choiceCards 下发卡面）。
/// </summary>
public class OP10_008_Scotch : IScriptedEffect
{
    public string CardNumber => "OP10-008";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方场上不存在"洛克"
        bool exists = me.Leader.Info.Name.Contains("洛克")
            || me.Characters.Any(c => c.Info.Name.Contains("洛克"));
        if (exists) return;

        // 从手牌选最多 1 张"洛克"登场
        var cands = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character && c.Info.Name.Contains("洛克"))
            .ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将我方手牌中最多 1 张\"洛克\"登场",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
    }
}
