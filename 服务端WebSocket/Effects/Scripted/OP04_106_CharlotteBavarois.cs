using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-106 夏洛特·巴巴洛亚（角色 / 光 / 大妈海盗团）
/// 【咚‼×1】我方生命卡牌张数少于对方的场合，此角色的力量 +1000。
/// 【触发】可以丢弃我方的 1 张手牌：此卡牌登场。
///
/// 实现说明：
///   - 【咚‼×1】持续力量：被赋予咚 ≥1 且我方生命 < 对方生命时此角色 +1000。
///     用 ContinuousEffect.PowerDelta + 谓词实现；OnEnterField 注册，离场引擎自动清理。
///   - 【触发】(OnLifeRevealTrigger)：可选成本=丢弃 1 张手牌；支付后此卡从废弃区登场。
/// </summary>
public class OP04_106_CharlotteBavarois : IScriptedEffect
{
    public string CardNumber => "OP04-106";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var selfId = self.Id;
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                PowerDelta = 1000,
                Predicate = (s, sideIdx, c) =>
                    c.Id == selfId && sideIdx == owner &&
                    s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                    s.Players[owner].LifeArea.Count < s.Players[1 - owner].LifeArea.Count,
            });
            return;
        }

        // 【触发】可选：丢弃 1 张手牌 → 此卡登场
        if (me.Hand.Count == 0) return;
        if (!me.Trash.Contains(self)) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "夏洛特·巴巴洛亚【触发】：丢弃我方 1 张手牌，将此卡牌登场？");
        if (!use) return;

        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方的 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (disc.Count == 0) return;
        var card = me.Hand.First(c => c.Id.ToString() == disc[0]);
        AtomicOps.DiscardHand(me, card);

        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, self);
    }
}
