using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-047 卓夫（角色）
/// 【咚!!×1】当通过此角色的攻击给予对方生命区伤害时，可以将我方卡组最上方 7 张放置废弃区。
///   —— 引擎无"给予生命伤害时"触发钩子(OnDealDamage)，此部分未实现。
/// 【登场时】将最多 1 张费用不高于 3 的角色放回其持有者的手牌，并可以将我方卡组最上方 2 张放置废弃区。
///
/// 实现说明：
///   - 仅实现【登场时】部分。目标为敌我任意一方费用≤3 的角色（放回其各自持有者手牌）。
///   - "并可以弃顶 2"为可选追加，用 ConfirmOptional。
/// </summary>
public class OP03_047_Zoff : IScriptedEffect
{
    public string CardNumber => "OP03-047";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 候选：敌我任意一方费用≤3 的角色
        var myCands = me.Characters
            .Where(c => ctx.State.CurrentCostOf(ctx.OwnerIndex, c) <= 3).ToList();
        var oppCands = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 3).ToList();
        var all = new List<CardInstance>();
        all.AddRange(myCands);
        all.AddRange(oppCands);

        if (all.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = all.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
                "将最多 1 张费用≤3 的角色放回其持有者的手牌",
                all.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var tgt = all.First(c => c.Id.ToString() == chosen[0]);
                int ownerOfTgt = myCands.Contains(tgt) ? ctx.OwnerIndex : 1 - ctx.OwnerIndex;
                AtomicOps.BounceToHand(ctx.State, ownerOfTgt, tgt);
            }
        }

        // 可以将我方卡组最上方 2 张放置废弃区
        if (me.Deck.Count > 0)
        {
            bool mill = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "是否将我方卡组最上方 2 张放置废弃区？");
            if (mill) AtomicOps.MillTop(me, 2);
        }
    }
}
