using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-048 希流。
/// 【登场时】将对方最多1张角色放回持有者手牌。之后，丢弃我方所有手牌。
/// </summary>
public sealed class OP14_048_Shiryu : IScriptedEffect
{
    public string CardNumber => "OP14-048";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opponent = ctx.State.Players[1 - ctx.OwnerIndex];

        if (opponent.Characters.Count > 0)
        {
            var candidates = opponent.Characters.ToList();
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多1张角色放回持有者手牌",
                candidates.Select(card => card.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var target = candidates.First(card => card.Id.ToString() == chosen[0]);
                AtomicOps.BounceToHand(ctx.State, 1 - ctx.OwnerIndex, target);
            }
        }

        foreach (var card in me.Hand.ToList())
            AtomicOps.DiscardHand(me, card);
    }
}
