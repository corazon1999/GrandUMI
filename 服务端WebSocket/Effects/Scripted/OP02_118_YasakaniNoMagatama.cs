using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-118 八尺琼勾玉（事件）
/// 【反击】可以丢弃我方的1张手牌：选择我方最多1张角色。本次战斗中，选中的角色不会被KO。
/// 【触发】将对方最多1张费用不高于3的舞台KO。
///
/// 实现说明：
///   - 【反击】(EventCounter)：可选成本"丢弃我方1张手牌"，支付后选我方最多1张角色，
///     用 ContinuousEffect.KoGuard="battle" + ThisBattle 期限赋予"本次战斗不会被KO"。
///     用 TurnCount 基准限制有效期（仅本回合战斗内）。
///   - 【触发】(OnLifeRevealTrigger)：对方舞台为单一字段 opp.StageCard，直接判其费用≤3后
///     询问是否KO。AtomicOps.KO 会自动清空 StageCard。
/// </summary>
public class OP02_118_YasakaniNoMagatama : IScriptedEffect
{
    public string CardNumber => "OP02-118";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 可选成本：丢弃我方1张手牌
            if (me.Hand.Count == 0) return;
            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "八尺琼勾玉【反击】：丢弃我方1张手牌，使我方最多1张角色本次战斗不会被KO？");
            if (!use) return;

            var discardExtra = new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var dch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "丢弃我方1张手牌（成本）", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
            if (dch.Count < 1) return;
            var dcard = me.Hand.First(c => c.Id.ToString() == dch[0]);
            AtomicOps.DiscardHand(me, dcard);

            // 效果：选我方最多1张角色，本次战斗不会被KO
            if (me.Characters.Count == 0) return;
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "选择我方最多1张角色，本次战斗不会被KO",
                me.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count < 1) return;
            var tgt = me.Characters.First(c => c.Id.ToString() == pick[0]);
            var tgtId = tgt.Id;
            int baseTurn = ctx.State.TurnCount;

            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = ctx.Source.Id.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                KoGuard = "battle",
                Predicate = (s, sideIdx, card) => card.Id == tgtId && s.TurnCount == baseTurn,
            });
            return;
        }

        // 【触发】：将对方最多1张费用≤3的舞台KO
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            var stage = opp.StageCard;
            if (stage is null || stage.Info.Cost > 3) return;
            bool ko = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "八尺琼勾玉【触发】：将对方费用≤3的舞台KO？");
            if (!ko) return;
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, stage);
        }
    }
}
