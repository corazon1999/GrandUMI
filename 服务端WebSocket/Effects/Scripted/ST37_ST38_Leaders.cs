using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>ST37 路飞：攻击抽两张，并登记独立于领航后续状态的回合末弃牌任务。</summary>
public sealed class ST37_001_Luffy : IScriptedEffect
{
    public string CardNumber => "ST37-001";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnAttackDeclare) return;
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);
        ctx.State.EndOfTurnTasks.Add(new EndTurnTask
        {
            Kind = "DiscardOwnHandToLimit",
            Owner = ctx.OwnerIndex,
            SourceCardId = ctx.Source.Id.ToString(),
            Count = 7,
        });
    }
}

/// <summary>ST38 克洛克达尔：攻击弃一抽一；每回合一次，KO合格的巴洛克角色抽一。</summary>
public sealed class ST38_001_Crocodile : IScriptedEffect, IActivatedMainAvailability, ITriggeredEffectAvailability
{
    public string CardNumber => "ST38-001";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnAttackDeclare or EffectTrigger.ActivatedMain;

    private static string OnceKey(CardInstance source) => $"ST38-001-act:{source.Id}";
    private static bool IsCostCharacter(CardInstance card)
        => card.Info.Cost >= 3 && card.Info.HasKeywordContaining("巴洛克工作室");

    public string? GetActivatedMainUnavailableReason(GameState state, int ownerIndex, CardInstance source)
    {
        var me = state.Players[ownerIndex];
        if (me.TurnOnceUsed.Contains(OnceKey(source))) return "克洛克达尔的启动主要本回合已经使用";
        if (!me.Characters.Any(IsCostCharacter)) return "需要原本费用至少3且特征包含《巴洛克工作室》的我方角色";
        return null;
    }

    public bool IsTriggerAvailable(GameState state, int ownerIndex, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger switch
        {
            EffectTrigger.OnAttackDeclare => state.Players[ownerIndex].Hand.Count > 0,
            EffectTrigger.ActivatedMain => GetActivatedMainUnavailableReason(state, ownerIndex, source) is null,
            _ => false,
        };

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnAttackDeclare)
        {
            if (me.Hand.Count == 0 || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                    "克洛克达尔【攻击时】：丢弃1张手牌，抽取1张卡牌？")) return;
            var discard = await ChooseOne(ctx, "OwnHandDiscard", "选择作为成本丢弃的1张手牌", me.Hand);
            if (discard is null || !me.Hand.Contains(discard)) return;
            bool previous = EffectRuntime.PayingCost;
            EffectRuntime.PayingCost = true;
            try { AtomicOps.DiscardHand(me, discard); }
            finally { EffectRuntime.PayingCost = previous; }
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        if (ctx.Trigger != EffectTrigger.ActivatedMain
            || GetActivatedMainUnavailableReason(ctx.State, ctx.OwnerIndex, ctx.Source) is not null
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "克洛克达尔【启动主要】：KO我方1张原本费用至少3且特征包含《巴洛克工作室》的角色，抽取1张卡牌？")) return;
        var cost = await ChooseOne(ctx, "OwnCharacterKOCost", "选择作为成本KO的角色", me.Characters.Where(IsCostCharacter));
        if (cost is null || !me.Characters.Contains(cost) || !IsCostCharacter(cost)) return;
        bool wasKOd = await AtomicOps.KOByEffectAsync(ctx.State, ctx.OwnerIndex, cost,
            ctx.Prompts, ctx.OwnerIndex, deferOnKO: true);
        if (!wasKOd) return;
        me.TurnOnceUsed.Add(OnceKey(ctx.Source));
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
    }

    private static async Task<CardInstance?> ChooseOne(EffectContext ctx, string kind, string text,
        IEnumerable<CardInstance> source)
    {
        var cards = source.ToList();
        if (cards.Count == 0) return null;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, kind, text,
            cards.Select(card => card.Id.ToString()).ToList(), 1, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = cards.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        return chosen.Count == 1 ? cards.FirstOrDefault(card => card.Id.ToString() == chosen[0]) : null;
    }
}
