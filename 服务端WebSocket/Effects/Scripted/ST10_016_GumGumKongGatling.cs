using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST10-016 橡皮橡皮猿王枪乱打。
/// 【触发】直到下个我方的回合结束时为止，我方最多1张领袖力量+1000。
///
/// 生命触发通常在对方回合发动，因此以之后第一个我方回合作为到期回合；
/// 当回合计数超过该回合后，持续力量自动失效。
/// </summary>
public sealed class ST10_016_GumGumKongGatling : IScriptedEffect
{
    public string CardNumber => "ST10-016";

    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var leader = ctx.State.Players[ctx.OwnerIndex].Leader;
        var chosen = await ctx.Prompts.ChooseCards(
            ctx.OwnerIndex,
            "OwnLeaderOrCharacter",
            "选择我方领袖，直到下个我方回合结束时力量+1000",
            new[] { leader.Id.ToString() },
            0,
            1);
        if (chosen.Count == 0) return;

        int expireTurn = ctx.State.CurrentTurnPlayer == ctx.OwnerIndex
            ? ctx.State.TurnCount
            : ctx.State.TurnCount + 1;
        var leaderId = leader.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = leaderId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = true,
                IncludeCharacters = false,
            },
            PowerDelta = 1000,
            Predicate = (state, sideIndex, card)
                => sideIndex == owner && card.Id == leaderId && state.TurnCount <= expireTurn,
        });
    }
}
