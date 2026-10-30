using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>路飞：登场恢复最多三张休息咚并限制对方角色休息，回合结束重置七水之城卡牌。</summary>
public sealed class OP18_118_Luffy : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-118";

    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnMyTurnEnd;

    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => state.Players[owner].Characters.Contains(source)
            && (trigger == EffectTrigger.OnEnterField
                || (trigger == EffectTrigger.OnMyTurnEnd && state.CurrentTurnPlayer == owner));

    public async Task Resolve(EffectContext ctx)
    {
        if (!IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            await AtomicOps.PromptChooseAndApplyDonCount(ctx.State, ctx.Prompts, ctx.OwnerIndex, 3,
                "路飞【登场时】：选择转为活跃状态的休息咚数量（最多3张）",
                don => don.State == DonState.Rest && don.AttachedToCardId is null,
                don => don.State = DonState.Active);

            // “之后”独立结算，即使没有休息咚或选择零张，也可以指定对方角色。
            var opponent = ctx.State.Players[1 - ctx.OwnerIndex];
            var targets = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OppCharacter",
                "选择对方最多1张角色，使其直到下个对方结束阶段结束时无法转为休息状态", opponent.Characters);
            if (targets.Count == 1 && opponent.Characters.Contains(targets[0]))
                AtomicOps.AddRestriction(targets[0], RestrictionKind.CannotBeRested,
                    KeywordDuration.UntilNextOpponentEndPhase, ctx.OwnerIndex);
            return;
        }

        bool Eligible(CardInstance card) => card.IsTapped && card.Info.HasKeyword("七水之城");
        var ownCards = me.Characters.Concat(me.StageCards).Prepend(me.Leader);
        var selected = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnRestCard",
            "将我方最多1张拥有《七水之城》特征的领袖、角色或舞台转为活跃状态", ownCards.Where(Eligible));
        if (selected.Count == 1 && Eligible(selected[0])
            && (selected[0] == me.Leader || me.Characters.Contains(selected[0]) || me.StageCards.Contains(selected[0])))
            AtomicOps.ActivateCard(selected[0]);
    }
}
