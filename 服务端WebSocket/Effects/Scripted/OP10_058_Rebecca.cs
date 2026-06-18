using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-058 莉贝卡（角色）
/// 【登场时】场上存在费用为 8 或更高的角色的场合，抽取 1 张卡牌。
/// 之后，从我方手牌中公开最多 2 张"莉贝卡"以外的拥有《德莱斯罗兹》特征且费用不高于 7 的角色卡牌。
/// 将公开卡牌中的 1 张登场，剩余的卡牌若为费用不高于 4 则以休息状态登场。
///
/// 实现说明 / 简化点：
///   - "场上存在费用≥8 的角色"检查双方所有角色（含领袖不计，领袖无费用语义）。
///   - "公开最多 2 张"用 ChooseCards(0..2)，公开后从中选 1 张正常登场，另一张若费用≤4 则以休息登场。
///   - 公开仍留在手牌，登场时从手牌打出（PlayFromHandFree）。
/// </summary>
public class OP10_058_Rebecca : IScriptedEffect
{
    public string CardNumber => "OP10-058";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 场上存在费用≥8 的角色 → 抽 1
        bool has8 = me.Characters.Concat(opp.Characters).Any(c => c.Info.Cost >= 8);
        if (has8)
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

        // 公开最多 2 张"莉贝卡"以外、《德莱斯罗兹》、费用≤7 的角色
        var pool = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            !c.Info.Name.Contains("莉贝卡") &&
            c.Info.HasKeyword("德莱斯罗兹") &&
            c.Info.Cost <= 7
        ).ToList();
        if (pool.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = pool.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var revealed = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RevealCharacters",
            "公开最多 2 张《德莱斯罗兹》且费用≤7 的角色（莉贝卡除外）",
            pool.Select(c => c.Id.ToString()).ToList(), 0, 2, extra);
        if (revealed.Count == 0) return;

        var revealedCards = pool.Where(c => revealed.Contains(c.Id.ToString())).ToList();

        // 将公开卡牌中的 1 张登场
        var playExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = revealedCards.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var playChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将公开卡牌中的 1 张登场",
            revealedCards.Select(c => c.Id.ToString()).ToList(), 1, 1, playExtra);
        if (playChosen.Count == 0) return;

        var playCard = revealedCards.First(c => c.Id.ToString() == playChosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, playCard);

        // 剩余的公开卡牌若费用≤4 则以休息状态登场
        foreach (var c in revealedCards)
        {
            if (c.Id == playCard.Id) continue;
            if (c.Info.Cost <= 4 && me.Hand.Contains(c))
            {
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, c);
                AtomicOps.RestCard(c);
            }
        }
    }
}
