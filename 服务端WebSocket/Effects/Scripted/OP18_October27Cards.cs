using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

internal static class October27CardHelpers
{
    // 两档目标都在移区前确定，不得把同一实例重复选入两档。
    public static async Task<List<(int side, CardInstance card)>> PickTwo(
        EffectContext ctx, int firstLimit, int secondLimit, bool originalPower)
    {
        var chosen = new List<(int side, CardInstance card, int limit)>();
        foreach (int limit in new[] { firstLimit, secondLimit })
        {
            bool Eligible(int side, CardInstance card) => !chosen.Any(item => item.card.Id == card.Id)
                && (originalPower ? ctx.State.OriginalPowerOf(side, card) : ctx.State.CurrentCostOf(side, card)) <= limit;
            var candidates = ctx.State.Players.SelectMany((player, side) => player.Characters
                .Where(card => (!originalPower || side != ctx.OwnerIndex) && Eligible(side, card))
                .Select(card => (side, card))).ToList();
            var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex,
                originalPower ? "OppCharacter" : "AnyCharacter",
                originalPower ? $"选择对方最多1张原本力量不高于{limit}的角色，使其无法攻击"
                    : $"选择最多1张费用不高于{limit}的角色，放回其持有者卡组最下方",
                candidates.Select(item => item.card));
            if (picked.Count != 1) continue;
            var target = candidates.First(item => item.card == picked[0]);
            if (ctx.State.Players[target.side].Characters.Contains(target.card) && Eligible(target.side, target.card))
                chosen.Add((target.side, target.card, limit));
        }
        return chosen.Where(item => ctx.State.Players[item.side].Characters.Contains(item.card)
                && (originalPower ? ctx.State.OriginalPowerOf(item.side, item.card)
                    : ctx.State.CurrentCostOf(item.side, item.card)) <= item.limit)
            .Select(item => (item.side, item.card)).ToList();
    }
}

/// <summary>佩鲁：阿拉巴斯坦领袖下持续获得速攻，登场弃低力量角色抽二并非KO送墓。</summary>
public sealed class OP18_013_Pell : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP18-013";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public Task RegisterFieldStatic(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex; var id = ctx.Source.Id;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == id.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = id.ToString(), Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
            GrantKeyword = "速攻",
            Predicate = (state, side, card) => side == owner && card.Id == id
                && state.Players[owner].Leader.Info.HasKeyword("阿拉巴斯坦王国"),
        });
        return Task.CompletedTask;
    }
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool Cost(CardInstance card) => card.Info.Kind == CardKind.Character && card.Info.Power <= 5000;
        var discarded = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnHandDiscard",
            "可以丢弃1张力量不高于5000的角色手牌，抽取2张卡牌，或取消发动", me.Hand.Where(Cost));
        if (discarded.Count != 1 || !me.Hand.Contains(discarded[0]) || !Cost(discarded[0])) return;
        bool previous = EffectRuntime.PayingCost; EffectRuntime.PayingCost = true;
        try { AtomicOps.DiscardHand(me, discarded[0]); }
        finally { EffectRuntime.PayingCost = previous; }
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);
        if (ctx.State.IsGameOver) return;
        int opponent = 1 - ctx.OwnerIndex;
        var targets = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OppCharacter",
            "将对方最多1张原本力量不高于3000的角色放置到废弃区",
            ctx.State.Players[opponent].Characters.Where(card => ctx.State.OriginalPowerOf(opponent, card) <= 3000));
        if (targets.Count == 1 && ctx.State.Players[opponent].Characters.Contains(targets[0])
            && ctx.State.OriginalPowerOf(opponent, targets[0]) <= 3000)
            await AtomicOps.ProcessEffectLeavesAsync(ctx.State, opponent, targets, ctx.Prompts, "trash",
                (state, side, card) => AtomicOps.TrashFieldCard(state, side, card));
    }
}

/// <summary>卡鲁事件：仅给结算时在场的阿拉巴斯坦角色加力；生命触发可给领袖或角色。</summary>
public sealed class OP18_018_LetsGoKaroo : IScriptedEffect
{
    public string CardNumber => "OP18-018";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.EventMain or EffectTrigger.OnLifeRevealTrigger;
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            AtomicOps.AddPowerToAllThisTurn(ctx.State, ctx.OwnerIndex,
                card => card.Info.HasKeyword("阿拉巴斯坦王国"), 1000, includeLeader: false);
        }
        else if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            var targets = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "使我方最多1张领袖或角色本回合力量+2000", me.Characters.Prepend(me.Leader));
            if (targets.Count == 1 && OctoberCardHelpers.IsOwnFieldCard(ctx, targets[0]))
                AtomicOps.AddPowerThisTurn(targets[0], 2000);
        }
    }
}

/// <summary>乔巴：攻击时每回合一次赋予领袖最多三张休息咚，重置自身并跳过下次重置。</summary>
public sealed class OP18_029_Chopper : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-029";
    private static string Key(CardInstance source) => $"OP18-029-attack:{source.Id}";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnAttackDeclare;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload) => state.CurrentTurnPlayer == owner
        && state.Players[owner].Characters.Contains(source) && !state.Players[owner].TurnOnceUsed.Contains(Key(source));
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnAttackDeclare
            || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "乔巴：赋予领袖最多3张休息咚，并将自身转为活跃状态？")) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        me.TurnOnceUsed.Add(Key(ctx.Source));
        await AtomicOps.PromptChooseAndApplyDonCount(ctx.State, ctx.Prompts, ctx.OwnerIndex, 3,
            "选择赋予我方领袖的休息咚数量（0至3张）", don => don.State == DonState.Rest,
            don => { don.State = DonState.Attached; don.AttachedToCardId = me.Leader.Id; });
        if (me.Characters.Contains(ctx.Source))
        {
            AtomicOps.ActivateCard(ctx.Source);
            ctx.Source.CannotActivateNextReset = true;
        }
    }
}

/// <summary>Mr.4与圣诞节：咚一持续加力，登场休息五咚后同时处理两档任意方角色。</summary>
public sealed class OP18_051_Mr4AndMissMerryChristmas : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP18-051";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public Task RegisterFieldStatic(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex; var id = ctx.Source.Id;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == id.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = id.ToString(), Scope = new ContinuousScope { Side = 0, IncludeLeader = false }, PowerDelta = 1000,
            Predicate = (state, side, card) => side == owner && card.Id == id
                && state.Players[owner].AttachedDonCount(id) >= 1,
        });
        return Task.CompletedTask;
    }
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        var dons = me.CostArea.Where(don => don.State == DonState.Active).Take(5).ToList();
        if (dons.Count != 5 || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "可以将5张活跃咚转为休息状态，令最多各1张费用不高于6和5的角色回卡组底？")
            || dons.Any(don => !me.CostArea.Contains(don) || don.State != DonState.Active)) return;
        foreach (var don in dons) don.State = DonState.Rest;
        var selected = await October27CardHelpers.PickTwo(ctx, 6, 5, originalPower: false);
        foreach (var group in selected.GroupBy(item => item.side))
        {
            var cards = group.Select(item => item.card).ToList();
            if (cards.Count > 1)
            {
                var ordered = await October9CardHelpers.Pick(ctx, group.Key, "OrderOwnCharacters",
                    "选择放回自己卡组底的顺序（先选者先放入）", cards, cards.Count, cards.Count);
                if (ordered.Count != cards.Count) continue;
                cards = ordered;
            }
            await AtomicOps.ProcessEffectLeavesAsync(ctx.State, group.Key,
                cards, ctx.Prompts, "deckBottom", AtomicOps.ReturnFieldToDeckBottom);
        }
    }
}

/// <summary>麒麟戈姆：我方登场返咚封攻至对方回合结束，KO时可返咚复活。</summary>
public sealed class OP18_064_Killingham : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-064";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnKO;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload) => trigger switch
        {
            EffectTrigger.OnEnterField => state.CurrentTurnPlayer == owner,
            EffectTrigger.OnKO => state.Players[owner].Trash.Contains(source),
            _ => false,
        };
    public async Task Resolve(EffectContext ctx)
    {
        if (!IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)
            || !await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            if (me.Trash.Contains(ctx.Source)) await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
            return;
        }
        var selected = await October27CardHelpers.PickTwo(ctx, 7000, 5000, originalPower: true);
        foreach (var (side, card) in selected)
            if (ctx.State.Players[side].Characters.Contains(card))
                AtomicOps.AddRestriction(card, RestrictionKind.CannotAttack,
                    KeywordDuration.UntilNextOpponentEndPhase, ctx.OwnerIndex);
    }
}

/// <summary>汤姆：返一咚并弃一手牌的成本全部完成后，才回收最多两张舞台。</summary>
public sealed class OP18_070_Tom : IScriptedEffect
{
    public string CardNumber => "OP18-070";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField
            || !await AtomicOps.PromptReturnOneDonAndDiscardOneHand(ctx, _ => true)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        var selected = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnTrash",
            "将我方废弃区中最多2张舞台卡牌加入手牌", me.Trash.Where(card => card.Info.Kind == CardKind.Stage), 0, 2);
        if (selected.Any(card => !me.Trash.Contains(card) || card.Info.Kind != CardKind.Stage)) return;
        foreach (var card in selected) { me.Trash.Remove(card); me.Hand.Add(card); }
    }
}

/// <summary>杰布拉：KO时可各自选择补生命与弃对方生命；CP领袖生命触发抽一并回底。</summary>
public sealed class OP18_103_Jabra : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-103";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnKO or EffectTrigger.OnLifeRevealTrigger;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger != EffectTrigger.OnLifeRevealTrigger || state.Players[owner].Leader.Info.HasKeywordContaining("CP");
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            if (me.Deck.Count > 0 && await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将我方卡组顶1张卡牌加入生命区顶？"))
                AtomicOps.AddLifeFromDeckTop(me, 1);
            if (ctx.State.Players[1 - ctx.OwnerIndex].LifeArea.Count > 0
                && await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将对方生命区顶1张卡牌放置到废弃区？"))
                OfficialCoverageHelpers.TrashLifeTop(ctx.State, 1 - ctx.OwnerIndex);
        }
        else if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger && me.Leader.Info.HasKeywordContaining("CP"))
        {
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
            if (!ctx.State.IsGameOver) await OctoberCardHelpers.MoveCharacter(ctx, 8, toDeckBottom: true);
        }
    }
}
