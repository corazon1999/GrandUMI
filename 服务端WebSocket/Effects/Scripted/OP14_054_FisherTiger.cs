using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-054 费雪·泰格（角色）
/// 【登场时】我方领袖拥有《鱼人族》特征的场合，抽3张卡牌。
/// 【我方的回合结束时】丢弃手牌，直到我方手牌变为5张。
/// </summary>
public sealed class OP14_054_FisherTiger : IScriptedEffect
{
    public string CardNumber => "OP14-054";

    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (me.Leader.Info.HasKeyword("鱼人族"))
                AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 3);
            return;
        }

        int discardCount = Math.Max(0, me.Hand.Count - 5);
        if (discardCount == 0) return;

        var candidates = me.Hand.ToList();
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(card => new
            {
                id = card.Id.ToString(),
                number = card.Info.Number,
            }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen",
            $"丢弃 {discardCount} 张手牌，直到手牌变为5张",
            candidates.Select(card => card.Id.ToString()).ToList(), discardCount, discardCount, extra);

        var selected = chosen.Count == discardCount
            ? chosen
            : candidates.Take(discardCount).Select(card => card.Id.ToString()).ToList();
        foreach (var id in selected)
        {
            var card = me.Hand.FirstOrDefault(item => item.Id.ToString() == id);
            if (card is not null) AtomicOps.DiscardHand(me, card);
        }
    }
}
