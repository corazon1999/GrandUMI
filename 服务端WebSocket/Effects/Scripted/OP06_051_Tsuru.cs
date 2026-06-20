using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-051 阿鹤（海军）（角色）
/// 【登场时】可以丢弃我方的 2 张手牌：对方将其 1 张角色放回持有者的手牌。
///
/// 实现说明 / 简化点：
///   - "可以丢弃 2 张手牌"为发动本效果的成本，与收益强耦合：先 ConfirmOptional 询问，
///     再让玩家选择并丢弃恰好 2 张手牌，成本支付完成后才执行退回对方角色。
///   - 手牌不足 2 张则无法支付成本，直接不发动。
///   - 退回对方角色由对方选择（对方将"其"1张角色放回），故选卡交由对方玩家执行。
/// </summary>
public class OP06_051_Tsuru : IScriptedEffect
{
    public string CardNumber => "OP06-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 手牌中可丢弃的牌（不含本卡本身，本卡已在场上）
        var discardable = me.Hand.Where(c => c.Id != self.Id).ToList();
        if (discardable.Count < 2) return;
        if (opp.Characters.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿鹤【登场时】：丢弃我方 2 张手牌，让对方将其 1 张角色放回手牌？");
        if (!use) return;

        // 成本：选择并丢弃 2 张手牌
        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = discardable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方 2 张手牌（作为成本）",
            discardable.Select(c => c.Id.ToString()).ToList(), 2, 2, discardExtra);
        if (chosen.Count < 2) return; // 未完成丢弃 → 成本未支付

        foreach (var id in chosen)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == id);
            if (card != null) AtomicOps.DiscardHand(me, card);
        }

        // 收益：对方将其 1 张角色放回持有者手牌（由对方选择）
        if (opp.Characters.Count == 0) return;
        var oppChars = opp.Characters.ToList();
        var bounce = await ctx.Prompts.ChooseCards(1 - ctx.OwnerIndex, "OwnCharacter",
            "将你的 1 张角色放回手牌",
            oppChars.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (bounce.Count > 0)
        {
            var target = oppChars.First(c => c.Id.ToString() == bounce[0]);
            AtomicOps.BounceToHand(ctx.State, 1 - ctx.OwnerIndex, target);
        }
    }
}
