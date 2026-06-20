using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-050 波尔·桑达索尼娅（水 / 九蛇海盗团）
/// 【登场时】我方领袖为“波尔·汉库珂”的场合，将我方手牌中最多 1 张费用不高于 3 的“波尔·汉库珂”登场。
///
/// 说明 / 简化点：
///   - 仅当我方领袖名为“波尔·汉库珂”时生效。
///   - 候选为手牌中费用 ≤ 3 且卡名为“波尔·汉库珂”的角色卡；最多 1 张（可不选）。
///   - 经 extra.choiceCards 下发卡面给客户端。
///   - 以 PlayFromHandFree 免费登场（不重复结算被登场卡的【登场时】，与引擎现有登场入口一致）。
/// </summary>
public class OP13_050_Sandersonia : IScriptedEffect
{
    public string CardNumber => "OP13-050";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方领袖为“波尔·汉库珂”
        if (!me.Leader.MatchesName("波尔·汉库珂")) return;

        // 候选：手牌中费用 ≤ 3 的角色“波尔·汉库珂”
        var candidates = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character
                        && c.Info.Cost <= 3
                        && c.MatchesName("波尔·汉库珂"))
            .ToList();
        if (candidates.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandHancock",
            "将手牌中最多 1 张费用不高于 3 的“波尔·汉库珂”登场",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var card = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
    }
}
