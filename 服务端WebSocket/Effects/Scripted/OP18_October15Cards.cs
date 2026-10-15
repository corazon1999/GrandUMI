using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>乔巴：KO后回收废弃区中力量不高于4000的角色。</summary>
public sealed class OP18_010_Chopper : IScriptedEffect
{
    public string CardNumber => "OP18-010";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnKO;
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool Eligible(CardInstance card) => card.Info.Kind == CardKind.Character
            && ctx.State.CurrentPowerOf(ctx.OwnerIndex, card) <= 4000;
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnTrashCharacter",
            "将废弃区中最多1张力量不高于4000的角色加入手牌", me.Trash.Where(Eligible));
        if (picked.Count == 1 && me.Trash.Contains(picked[0]) && Eligible(picked[0]))
            AtomicOps.TrashToHand(me, picked[0]);
    }
}

/// <summary>巴奇：登场或KO时，冻结对方休息且当前力量不高于8000的角色一次重置。</summary>
public sealed class OP18_032_Buggy : IScriptedEffect
{
    public string CardNumber => "OP18-032";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnKO;
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger)) return;
        int opponent = 1 - ctx.OwnerIndex;
        var characters = ctx.State.Players[opponent].Characters;
        bool Eligible(CardInstance card) => card.IsTapped && ctx.State.CurrentPowerOf(opponent, card) <= 8000;
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OppCharacter",
            "使对方最多1张休息状态且力量不高于8000的角色下个重置阶段无法转为活跃", characters.Where(Eligible));
        if (picked.Count == 1 && characters.Contains(picked[0]) && Eligible(picked[0]))
            AtomicOps.PreventActivateNextReset(picked[0]);
    }
}

/// <summary>Mr.5与Miss.情人节：赋予至少1咚时持续加1000；登场可登场低费用巴洛克角色。</summary>
public sealed class OP18_052_Mr5Valentine : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP18-052";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public Task RegisterFieldStatic(EffectContext ctx)
    {
        string id = ctx.Source.Id.ToString();
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == id);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect {
            SourceCardId = id, Scope = new ContinuousScope { Side = 0, IncludeLeader = false }, PowerDelta = 1000,
            Predicate = (state, side, card) => side == owner && card == ctx.Source
                && state.Players[owner].CostArea.Any(don => don.State == DonState.Attached && don.AttachedToCardId == card.Id),
        });
        return Task.CompletedTask;
    }
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool Eligible(CardInstance card) => card.Info.Kind == CardKind.Character
            && card.Info.HasKeywordContaining("巴洛克工作室") && ctx.State.CurrentCostOf(ctx.OwnerIndex, card) <= 3;
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnHandCharacter",
            "登场手牌中最多1张费用不高于3且特征包含《巴洛克工作室》的角色", me.Hand.Where(Eligible));
        if (picked.Count == 1 && me.Hand.Contains(picked[0]) && Eligible(picked[0]))
            await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked[0]);
    }
}

/// <summary>基威与摩兹：实际KO低费用舞台作为成本，追加最多1张休息咚。</summary>
public sealed class OP18_063_KiwiMozu : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-063";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => state.Players[owner].StageCards.Any(card => state.CurrentCostOf(owner, card) <= 5);
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool Eligible(CardInstance card) => ctx.State.CurrentCostOf(ctx.OwnerIndex, card) <= 5;
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnStage",
            "可以KO我方1张费用不高于5的舞台作为成本，或取消发动", me.StageCards.Where(Eligible));
        if (picked.Count != 1 || !me.StageCards.Contains(picked[0]) || !Eligible(picked[0])) return;
        bool previous = EffectRuntime.PayingCost;
        bool paid;
        EffectRuntime.PayingCost = true;
        try { paid = await AtomicOps.KOByEffectAsync(ctx.State, ctx.OwnerIndex, picked[0], ctx.Prompts, ctx.OwnerIndex, deferOnKO: true); }
        finally { EffectRuntime.PayingCost = previous; }
        if (paid && me.DonDeck.Count > 0
            && await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "从咚!!卡组追加1张休息状态的咚？"))
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }
}

/// <summary>刻耳贝洛斯：自身退手支付成本，使夏姆洛克圣本回合获得三选一能力。</summary>
public sealed class OP18_085_Cerberus : IScriptedEffect, IActivatedMainAvailability
{
    public string CardNumber => "OP18-085";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;
    public string? GetActivatedMainUnavailableReason(GameState state, int owner, CardInstance source)
        => state.Players[owner].Characters.Contains(source) && !state.IsLeaveGuarded(source, "effect")
            ? null : "刻耳贝洛斯必须在场上且能够放回手牌";
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger) || ctx.State.CurrentTurnPlayer != ctx.OwnerIndex
            || GetActivatedMainUnavailableReason(ctx.State, ctx.OwnerIndex, ctx.Source) is not null
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将刻耳贝洛斯放回手牌，使夏姆洛克圣本回合获得一种能力？")
            || GetActivatedMainUnavailableReason(ctx.State, ctx.OwnerIndex, ctx.Source) is not null) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        try
        {
            if (await AtomicOps.TryEffectLeaveGuard(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Prompts, "hand")) return;
            AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, ctx.Source);
        }
        finally { EffectRuntime.PayingCost = previous; }
        if (me.Characters.Contains(ctx.Source) || !me.Hand.Contains(ctx.Source)) return;
        bool Eligible(CardInstance card) => card.MatchesName("夏姆洛克圣");
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnCharacter",
            "使我方最多1张夏姆洛克圣本回合获得速攻、流放或不可阻挡", me.Characters.Where(Eligible));
        if (picked.Count != 1) return;
        string[] keywords = ["速攻", "流放", "不可阻挡"];
        int choice = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择本回合获得的能力", keywords);
        if (choice is < 0 or > 2 || !me.Characters.Contains(picked[0]) || !Eligible(picked[0])) return;
        AtomicOps.GiveKeyword(picked[0], keywords[choice], KeywordDuration.ThisTurn);
    }
}

/// <summary>脸谱：KO时正面补CP生命；CP领袖生命触发可KO或休息对方低费用角色。</summary>
public sealed class OP18_101_Kumadori : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-101";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnKO or EffectTrigger.OnLifeRevealTrigger;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger != EffectTrigger.OnLifeRevealTrigger || state.Players[owner].Leader.Info.HasKeywordContaining("CP");
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger) || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnHandLife",
                "将手牌中最多1张特征包含CP的卡牌正面加入生命区", me.Hand.Where(card => card.Info.HasKeywordContaining("CP")));
            if (picked.Count != 1) return;
            int position = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择正面生命卡放置的位置", ["生命区最上方", "生命区最下方"]);
            if (position is not (0 or 1) || !me.Hand.Contains(picked[0]) || !picked[0].Info.HasKeywordContaining("CP")) return;
            AtomicOps.HandToLife(me, picked[0], toTop: position == 0, faceUp: true);
            return;
        }
        int mode = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择脸谱的触发效果", ["KO对方最多1张费用不高于6的角色", "休息对方最多1张费用不高于6的角色"]);
        if (mode is not (0 or 1)) return;
        int opponent = 1 - ctx.OwnerIndex;
        var characters = ctx.State.Players[opponent].Characters;
        bool Eligible(CardInstance card) => ctx.State.CurrentCostOf(opponent, card) <= 6
            && (mode == 0 || (!card.IsTapped && AtomicOps.CanRestCard(ctx.State, card, opponent)));
        var targets = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OppCharacter",
            mode == 0 ? "KO对方最多1张费用不高于6的角色" : "休息对方最多1张费用不高于6的角色", characters.Where(Eligible));
        if (targets.Count != 1 || !characters.Contains(targets[0]) || !Eligible(targets[0])
            || !me.Leader.Info.HasKeywordContaining("CP")) return;
        if (mode == 0) await AtomicOps.KOByEffectAsync(ctx.State, opponent, targets[0], ctx.Prompts, ctx.OwnerIndex);
        else AtomicOps.RestCard(ctx.State, targets[0], opponent);
    }
}
