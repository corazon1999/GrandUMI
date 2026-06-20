using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-108 夏洛特·莫斯卡特（角色 / 光 / 大妈海盗团）
/// 【咚‼×1】此角色获得【流放】效果。
/// 【触发】可以丢弃我方的 1 张手牌：此卡牌登场。
///
/// 实现说明：
///   - 【咚‼×1】持续赋予【流放】：被赋予咚 ≥1 时此角色获得【流放】。
///     用 ContinuousEffect.GrantKeyword="流放" + 谓词实现；【流放】已被引擎消费。
///   - 【触发】(OnLifeRevealTrigger)：可选成本=丢弃 1 张手牌；支付后此卡从废弃区登场。
/// </summary>
public class OP04_108_CharlotteMoscato : IScriptedEffect
{
    public string CardNumber => "OP04-108";

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
                GrantKeyword = "流放",
                Predicate = (s, sideIdx, c) =>
                    c.Id == selfId && sideIdx == owner &&
                    s.Players[owner].AttachedDonCount(selfId) >= 1,
            });
            return;
        }

        // 【触发】可选：丢弃 1 张手牌 → 此卡登场
        if (me.Hand.Count == 0) return;
        if (!me.Trash.Contains(self)) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "夏洛特·莫斯卡特【触发】：丢弃我方 1 张手牌，将此卡牌登场？");
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
