using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>OP11-118 蒙奇·D·路飞：攻击时可将任一方费用不高于4的角色退回其持有者手牌。</summary>
public sealed class OP11_118_Luffy : IScriptedEffect
{
    public string CardNumber => "OP11-118";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.State.CurrentBattle is not { } battle || battle.AttackerCardId != ctx.Source.Id) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Hand.Count == 0 || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "丢弃1张手牌，发动路飞的攻击时效果？")) return;

        var discarded = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃1张手牌作为成本", me.Hand.Select(card => card.Id.ToString()).ToList(), 1, 1);
        var cost = me.Hand.FirstOrDefault(card => discarded.Contains(card.Id.ToString()));
        if (cost is null) return;
        AtomicOps.DiscardHand(me, cost);

        var candidates = ctx.State.Players
            .SelectMany((player, owner) => player.Characters
                .Where(card => ctx.State.CurrentCostOf(owner, card) <= 4)
                .Select(card => (owner, card)))
            .ToList();
        if (candidates.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacterCostLe4",
                "将最多1张费用不高于4的角色放回其持有者的手牌",
                candidates.Select(item => item.card.Id.ToString()).ToList(), 0, 1);
            var target = candidates.FirstOrDefault(item => chosen.Contains(item.card.Id.ToString()));
            if (target.card is not null && !await AtomicOps.TryEffectLeaveGuard(
                    ctx.State, target.owner, target.card, ctx.Prompts, "hand"))
                AtomicOps.BounceToHand(ctx.State, target.owner, target.card);
        }

        if (!me.CostArea.Any(don => don.State == DonState.Rest)) return;
        var donTargets = new List<CardInstance> { me.Leader };
        donTargets.AddRange(me.Characters);
        var attach = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择我方最多1张领袖或角色，赋予1张休息状态的咚!!",
            donTargets.Select(card => card.Id.ToString()).ToList(), 0, 1);
        var attachTarget = donTargets.FirstOrDefault(card => attach.Contains(card.Id.ToString()));
        if (attachTarget is not null)
            AtomicOps.AttachDonFromCost(me, attachTarget.Id, 1, DonState.Rest);
    }
}
