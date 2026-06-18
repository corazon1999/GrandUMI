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
///   - "直到下个我方回合开始时" 用 TurnCount 限定有效期：登场在我方回合(baseTurn)，覆盖对方回合(baseTurn+1)，
///     我方下个回合开始(baseTurn+2)时失效 → Predicate 取 s.TurnCount <= baseTurn + 1。
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

        // 我方所有角色直到下个我方回合开始前不会因效果被 KO
        var selfId = self.Id;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "effect",
            Predicate = (s, sideIdx, c) => sideIdx == owner && s.TurnCount <= baseTurn + 1,
        });

        // 之后：抽 2 张，丢弃 2 张手牌
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

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
