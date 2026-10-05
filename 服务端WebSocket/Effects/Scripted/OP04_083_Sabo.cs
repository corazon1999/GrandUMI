using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-083 萨波（角色 / 地 / 德莱斯罗兹·革命军）
/// 【阻挡者】（关键词，由引擎处理）
/// 【登场时】直到下个我方的回合开始时为止，我方的所有角色不会因效果而被 KO。
///   之后，抽取 2 张卡牌，丢弃我方的 2 张手牌。
///
/// 实现说明：
///   - "不会因效果被 KO" = ContinuousEffect.KoGuard="effect"，Scope 我方全体角色。
///   - 固定结算时的角色和到期回合；来源离场或被无效不撤销保护，目标离场则结束自身保护。
///   - 之后抽 2 弃 2：Draw(2) 后让玩家选 2 张手牌丢弃。
/// </summary>
public class OP04_083_Sabo : IScriptedEffect
{
    public string CardNumber => "OP04-083";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        int baseTurn = ctx.State.TurnCount;

        // 只保护结算时在场的角色；已结算的保护不依赖萨博继续留场或保持效果有效。
        var selfId = self.Id;
        foreach (var character in me.Characters) character.FieldSnapshotSourceIds.Add(selfId);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            SourceCardNumber = self.Info.Number,
            PersistsAfterSourceLeaves = true,
            ExpiresAfterTurnCount = baseTurn + (ctx.State.CurrentTurnPlayer == owner ? 1 : 0),
            Scope = new ContinuousScope { Side = -1, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "effect",
            Predicate = (s, sideIdx, c) => sideIdx == owner && c.FieldSnapshotSourceIds.Contains(selfId),
        });

        // 之后：抽 2 张，丢弃 2 张手牌
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);

        int toDiscard = Math.Min(2, me.Hand.Count);
        if (toDiscard <= 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方的 2 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), toDiscard, toDiscard);
        foreach (var id in chosen)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == id);
            if (card != null) AtomicOps.DiscardHand(me, card);
        }
    }
}
