using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-053 波尔萨利诺（角色）
/// 【对方的回合中】我方领袖拥有《海军》特征的场合，此角色力量+1000、并获得【阻挡者】效果。
///   —— 通过单个 ContinuousEffect 注册（PowerDelta+1000 + GrantKeyword="阻挡者"），同一条件评估。
///
/// 【每回合1次】因对方效果将要离场时，可弃1张手牌使自身不离场；覆盖效果KO与非KO离场。
/// </summary>
public class OP12_053_Borsalino : IScriptedEffect
{
    public string CardNumber => "OP12-053";

    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.OnEnterField or EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var selfId = self.Id;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                PowerDelta = 1000,
                GrantKeyword = "阻挡者",
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId &&
                    s.CurrentTurnPlayer != owner &&
                    s.Players[owner].Leader.Info.HasKeyword("海军"),
            });
            return;
        }

        bool nonKoLeave = ctx.Trigger == EffectTrigger.OnAllyWillLeaveField;
        if (!nonKoLeave &&
            (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex)) return;
        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        var victimOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int oi ? oi : -1;
        if (victimOwner != ctx.OwnerIndex || victimId != selfId.ToString()) return;

        var key = self.Info.Number + "-guard:" + self.Id;
        if (me.TurnOnceUsed.Contains(key) || me.Hand.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "波尔萨利诺【每回合1次】：丢弃1张手牌，使此角色不离场？")) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃1张手牌作为离场替代成本",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var discard = me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (discard is null) return;
        AtomicOps.DiscardHand(me, discard);
        if (nonKoLeave) ctx.State.MarkPreventLeave(selfId);
        else ctx.State.MarkPreventKO(selfId);
        me.TurnOnceUsed.Add(key);
    }
}
