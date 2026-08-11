using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST17-002 特拉法尔加·罗
/// 【登场时】可以将我方 1 张角色放回手牌：领袖具有《王下七武海》特征时，
/// 将最多 1 张费用不高于 4 的角色放回持有者手牌。
/// </summary>
public sealed class ST17_002_TrafalgarLaw : IScriptedEffect
{
    public string CardNumber => "ST17-002";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Leader.Info.HasKeyword("王下七武海") || me.Characters.Count == 0) return;

        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将我方 1 张角色放回手牌，使最多 1 张费用不高于 4 的角色放回持有者手牌？"))
            return;

        var costIds = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方 1 张角色放回手牌", me.Characters.Select(card => card.Id.ToString()).ToList(), 1, 1);
        var cost = me.Characters.FirstOrDefault(card => card.Id.ToString() == costIds.FirstOrDefault());
        if (cost is null) return;
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, cost);

        var candidates = ctx.State.Players
            .SelectMany((player, side) => player.Characters
                .Where(card => ctx.State.CurrentCostOf(side, card) <= 4)
                .Select(card => (side, card)))
            .ToList();
        if (candidates.Count == 0) return;

        var targetIds = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "将最多 1 张费用不高于 4 的角色放回持有者手牌",
            candidates.Select(item => item.card.Id.ToString()).ToList(), 0, 1);
        var target = candidates.FirstOrDefault(item => item.card.Id.ToString() == targetIds.FirstOrDefault());
        if (target.card is null) return;
        if (!await AtomicOps.TryEffectLeaveGuard(ctx.State, target.side, target.card, ctx.Prompts, "hand"))
            AtomicOps.BounceToHand(ctx.State, target.side, target.card);
    }
}
