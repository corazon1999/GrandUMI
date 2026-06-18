using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-018 达斯琪（角色 / 风 / 海军）
/// 【对方的回合中】此角色不会因对方的效果而被KO，并获得【阻挡者】效果。
/// 【我方的回合结束时】可以将我方的1张咚!!转为休息状态，并丢弃我方的1张手牌：将此角色转为活跃状态。
///
/// 实现说明：
///   - 对方回合中的「不因对方效果被KO」用 ContinuousEffect.KoGuard="effect"，Predicate 限对方回合且为自身；
///     「获得【阻挡者】」用同 SourceCard 的另一条 ContinuousEffect.GrantKeyword="阻挡者"，同样限对方回合且自身。
///     两条均 OnEnterField 注册，先 RemoveAll 防重复。
///   - 我方回合结束时（OnMyTurnEnd）：可选成本=横置我方1张活跃咚!! + 丢弃1张手牌 → 将此角色转为活跃状态。
/// </summary>
public class EB03_018_Tashigi : IScriptedEffect
{
    public string CardNumber => "EB03-018";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

            // 【对方的回合中】此角色不会因对方的效果而被KO
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                KoGuard = "effect",
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId && s.CurrentTurnPlayer != owner,
            });

            // 【对方的回合中】此角色获得【阻挡者】
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "阻挡者",
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId && s.CurrentTurnPlayer != owner,
            });

            return;
        }

        if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            if (!self.IsTapped) return; // 已活跃则无意义

            var activeDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
            if (activeDon is null) return;   // 无活跃咚可横置 → 无法支付成本
            if (me.Hand.Count == 0) return;  // 无手牌可弃 → 无法支付成本

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "达斯琪【我方的回合结束时】：横置我方1张咚!!并丢弃1张手牌，将此角色转为活跃状态？");
            if (!use) return;

            // 成本1：将我方1张活跃咚!!转为休息状态
            activeDon.State = DonState.Rest;

            // 成本2：丢弃我方1张手牌（玩家选择）
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "丢弃1张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (ch.Count < 1) return;
            var disc = me.Hand.First(c => c.Id.ToString() == ch[0]);
            AtomicOps.DiscardHand(me, disc);

            // 收益：将此角色转为活跃状态
            AtomicOps.ActivateCard(self);
            return;
        }
    }
}
