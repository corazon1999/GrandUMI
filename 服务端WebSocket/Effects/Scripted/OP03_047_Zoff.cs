using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-047 卓夫（角色）
/// 【咚!!×1】当通过此角色的攻击给予对方生命区伤害时，可以将我方卡组最上方 7 张放置废弃区。
/// 【登场时】将最多 1 张费用不高于 3 的角色放回其持有者的手牌，并可以将我方卡组最上方 2 张放置废弃区。
///
/// 实现说明：
///   - OnDamageToLeader 校验攻击者为本卡且附有咚×1，可选弃顶7张。
///   - 【登场时】目标为敌我任意一方费用≤3的角色（放回其各自持有者手牌）。
///   - "并可以弃顶 2"为可选追加，用 ConfirmOptional。
/// </summary>
public class OP03_047_Zoff : IScriptedEffect
{
    public string CardNumber => "OP03-047";

    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.OnEnterField or EffectTrigger.OnDamageToLeader;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnDamageToLeader)
        {
            var attackerId = ctx.Vars.TryGetValue("attackerId", out var av) ? av as string : null;
            if (attackerId != ctx.Source.Id.ToString() || me.AttachedDonCount(ctx.Source.Id) < 1 || me.Deck.Count == 0) return;
            if (await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "卓夫：将我方卡组最上方7张放置废弃区？"))
                AtomicOps.MillTop(me, 7);
            return;
        }

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
