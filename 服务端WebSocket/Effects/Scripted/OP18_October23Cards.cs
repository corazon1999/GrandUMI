using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>佐罗的两段静态能力分别按当前费用与废弃区数量判断，不属于登场时效果。</summary>
public sealed class OP18_092_Zoro : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP18-092";
    public bool HandlesTrigger(EffectTrigger trigger) => false;
    public Task Resolve(EffectContext ctx) => Task.CompletedTask;

    public Task RegisterFieldStatic(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        var id = ctx.Source.Id;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == id.ToString());
        ctx.State.ContinuousEffects.AddRange([
            new ContinuousEffect
            {
                SourceCardId = id.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
                PowerDelta = 3000,
                Predicate = (state, side, card) => side == owner && card.Id == id
                    && state.Players.SelectMany(player => player.Characters)
                        .Any(character => state.CurrentCostOf(character) >= 12),
            },
            new ContinuousEffect
            {
                SourceCardId = id.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
                GrantKeyword = "速攻",
                Predicate = (state, side, card) => side == owner && card.Id == id
                    && state.Players[owner].Trash.Count >= 10,
            },
        ]);
        return Task.CompletedTask;
    }
}

/// <summary>范克弗里德：登场可弃CP手牌抽二；主要阶段可休息自身为我方斯巴达姆加力。</summary>
public sealed class OP18_108_Funkfreed : IScriptedEffect, IActivatedMainAvailability
{
    public string CardNumber => "OP18-108";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnEnterField or EffectTrigger.ActivatedMain;

    public string? GetActivatedMainUnavailableReason(GameState state, int owner, CardInstance source)
        => state.CurrentTurnPlayer != owner ? "只能在我方主要阶段发动"
            : !state.Players[owner].Characters.Contains(source) ? "范克弗里德必须在角色区"
            : source.IsTapped || !AtomicOps.CanRestCard(state, source, owner) ? "此角色必须能够转为休息状态"
            : null;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnHandDiscard",
                "可以丢弃1张特征包含《CP》的手牌作为成本，抽取2张卡牌，或取消发动",
                me.Hand.Where(card => card.Info.HasKeywordContaining("CP")));
            if (picked.Count != 1 || !me.Hand.Contains(picked[0]) || !picked[0].Info.HasKeywordContaining("CP")) return;
            bool previous = EffectRuntime.PayingCost;
            EffectRuntime.PayingCost = true;
            try { AtomicOps.DiscardHand(me, picked[0]); }
            finally { EffectRuntime.PayingCost = previous; }
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);
            return;
        }
        if (ctx.Trigger != EffectTrigger.ActivatedMain
            || GetActivatedMainUnavailableReason(ctx.State, ctx.OwnerIndex, ctx.Source) is not null
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将范克弗里德转为休息状态，使我方最多1张斯巴达姆本回合力量+1000？")
            || GetActivatedMainUnavailableReason(ctx.State, ctx.OwnerIndex, ctx.Source) is not null) return;

        bool previousCost = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        bool paid;
        try { paid = AtomicOps.RestCard(ctx.State, ctx.Source, ctx.OwnerIndex); }
        finally { EffectRuntime.PayingCost = previousCost; }
        if (!paid) return;

        var targets = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "使我方最多1张斯巴达姆本回合力量+1000",
            me.Characters.Prepend(me.Leader).Where(card => card.MatchesName("斯巴达姆")));
        if (targets.Count == 1 && (me.Leader == targets[0] || me.Characters.Contains(targets[0]))
            && targets[0].MatchesName("斯巴达姆")) AtomicOps.AddPowerThisTurn(targets[0], 1000);
    }
}
