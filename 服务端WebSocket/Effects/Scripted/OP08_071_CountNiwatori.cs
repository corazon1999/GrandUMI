using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-071 公鸡伯爵（角色 / 暗 6 费 7000，大妈海盗团）
/// 【对方的回合中】【KO时】咚!!-1（可以将我方场上指定数量的咚‼放回咚‼卡组）：
///   从我方卡组中将最多 1 张费用不高于 4 的"蛋蛋男爵"登场。之后，切洗卡组。
///
/// 实现说明：
///   - 触发：OnKO（此卡被 KO 时）。【对方的回合中】通过判断 CurrentTurnPlayer != owner 限定。
///   - 成本：咚!!-1（ReturnDonToDeck(me, 1)）。需我方场上有咚可放回，否则不发动；
///     用 ConfirmOptional 询问是否发动并支付成本。
///   - 收益：从卡组登场最多 1 张费用≤4 的"蛋蛋男爵"，之后切洗卡组。
/// </summary>
public class OP08_071_CountNiwatori : IScriptedEffect
{
    public string CardNumber => "OP08-071";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【对方的回合中】限定
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;

        // 成本前置：需有咚可放回
        int donCount = me.CostArea.Count;
        if (donCount < 1) return;

        // 候选：卡组中费用≤4 的"蛋蛋男爵"
        var cands = me.Deck
            .Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 4 && c.MatchesName("蛋蛋男爵"))
            .ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "公鸡伯爵【KO时】：咚!!-1，从卡组登场 1 张费用≤4 的\"蛋蛋男爵\"？");
        if (!use) return;

        // 成本：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DeckCharacter",
            "从卡组登场最多 1 张费用≤4 的\"蛋蛋男爵\"",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            me.Deck.Remove(picked);
            if (me.Characters.Count >= 5)
            {
                var sacrifice = me.Characters[0];
                me.Characters.RemoveAt(0);
                me.Trash.Add(sacrifice);
            }
            picked.TurnPlayed = ctx.State.TurnCount;
            me.Characters.Add(picked);
        }

        // 之后切洗卡组
        ctx.Engine?.ShuffleDeck(me, ctx.OwnerIndex, "OP08-071_ko");
    }
}
