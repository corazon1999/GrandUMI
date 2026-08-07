using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-120 克洛克达尔（角色）
/// 【登场时】直到下个对方的结束阶段结束时为止，对方最多 1 张当前费用不高于 9 的角色无法攻击。
/// 之后，对方场上存在当前费用为 0 或当前费用为 8 以上的角色时，抽取 1 张卡牌。
/// 【KO时】可以丢弃我方的 1 张手牌：从废弃区中登场此角色卡牌。
/// </summary>
public class OP14_120_Crocodile : IScriptedEffect
{
    public string CardNumber => "OP14-120";

    public bool HandlesTrigger(EffectTrigger trigger) =>
        trigger is EffectTrigger.OnEnterField or EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var opponentIndex = 1 - ctx.OwnerIndex;
            var opponent = ctx.State.Players[opponentIndex];
            var candidates = opponent.Characters
                .Where(card => ctx.State.CurrentCostOf(opponentIndex, card) <= 9)
                .ToList();

            if (candidates.Count > 0)
            {
                var extra = new Dictionary<string, object?>
                {
                    ["choiceCards"] = candidates
                        .Select(card => new { id = card.Id.ToString(), number = card.Info.Number })
                        .ToList(),
                };
                var selectedTargets = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                    "选择对方最多 1 张当前费用不高于 9 的角色，使其直到下个对方结束阶段无法攻击",
                    candidates.Select(card => card.Id.ToString()).ToList(), 0, 1, extra);
                var target = selectedTargets.Count > 0
                    ? candidates.FirstOrDefault(card => card.Id.ToString() == selectedTargets[0])
                    : null;
                if (target is not null)
                {
                    AtomicOps.AddRestriction(target, RestrictionKind.CannotAttack,
                        KeywordDuration.UntilNextOpponentEndPhase, ctx.OwnerIndex);
                }
            }

            var shouldDraw = opponent.Characters.Any(card =>
            {
                var currentCost = ctx.State.CurrentCostOf(opponentIndex, card);
                return currentCost == 0 || currentCost >= 8;
            });
            if (shouldDraw)
                AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

            return;
        }

        // 【KO时】结算时，本卡必须仍在我方废弃区。
        if (!me.Trash.Contains(ctx.Source) || me.Hand.Count == 0) return;

        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "克洛克达尔【KO时】：丢弃我方 1 张手牌，从废弃区登场此角色？"))
            return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方 1 张手牌",
            me.Hand.Select(card => card.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count < 1) return;

        var discard = me.Hand.FirstOrDefault(card => card.Id.ToString() == chosen[0]);
        if (discard is null) return;

        AtomicOps.DiscardHand(me, discard);
        await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
    }
}
