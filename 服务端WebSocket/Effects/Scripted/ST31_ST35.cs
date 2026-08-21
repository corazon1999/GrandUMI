using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>ST31-ST35 starter-deck effects released as a single rules batch.</summary>
public abstract class ST31To35EffectBase : IScriptedEffect
{
    public abstract string CardNumber { get; }

    public bool HandlesTrigger(EffectTrigger trigger) => CardNumber switch
    {
        "ST31-001" or "ST31-002" or "ST31-003" or "ST31-004" => trigger == EffectTrigger.OnEnterField,
        "ST31-005" => trigger is EffectTrigger.OnEnterField or EffectTrigger.ActivatedMain,
        "ST32-001" or "ST32-002" or "ST32-004" or "ST32-005" => trigger == EffectTrigger.OnEnterField,
        "ST32-003" => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnCharRested,
        "ST33-001" or "ST33-003" or "ST33-005" => trigger == EffectTrigger.OnEnterField,
        "ST33-002" => trigger is EffectTrigger.OnAttackDeclare or EffectTrigger.OnKO,
        "ST33-004" => false,
        "ST34-001" => trigger is EffectTrigger.OnDonReturnedToDeck or EffectTrigger.OnKO,
        "ST34-002" or "ST34-003" or "ST34-004" => trigger == EffectTrigger.OnEnterField,
        "ST34-005" => trigger == EffectTrigger.OnAttackDeclare,
        "ST35-001" or "ST35-002" or "ST35-004" or "ST35-005" => trigger == EffectTrigger.OnEnterField,
        "ST35-003" => trigger == EffectTrigger.OnAttackDeclare,
        _ => false,
    };

    public Task Resolve(EffectContext ctx) => CardNumber switch
    {
        "ST31-001" => ST31_001(ctx), "ST31-002" => ST31_002(ctx),
        "ST31-003" => ST31_003(ctx), "ST31-004" => ST31_004(ctx), "ST31-005" => ST31_005(ctx),
        "ST32-001" => ST32_001(ctx), "ST32-002" => ST32_002(ctx),
        "ST32-003" => ST32_003(ctx), "ST32-004" => ST32_004(ctx), "ST32-005" => ST32_005(ctx),
        "ST33-001" => ST33_001(ctx), "ST33-002" => ST33_002(ctx),
        "ST33-003" => ST33_003(ctx), "ST33-005" => ST33_005(ctx),
        "ST34-001" => ST34_001(ctx), "ST34-002" => ST34_002(ctx),
        "ST34-003" => ST34_003(ctx), "ST34-004" => ST34_004(ctx), "ST34-005" => ST34_005(ctx),
        "ST35-001" => ST35_001(ctx), "ST35-002" => ST35_002(ctx),
        "ST35-003" => ST35_003(ctx), "ST35-004" => ST35_004(ctx), "ST35-005" => ST35_005(ctx),
        _ => Task.CompletedTask,
    };

    private static Dictionary<string, object?> ChoiceCards(IEnumerable<CardInstance> cards) => new()
    {
        ["choiceCards"] = cards.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
    };

    private static async Task<CardInstance?> Choose(EffectContext ctx, int player, string kind, string text,
        IReadOnlyList<CardInstance> cards, int min = 0)
    {
        if (cards.Count == 0) return null;
        var chosen = await ctx.Prompts.ChooseCards(player, kind, text,
            cards.Select(c => c.Id.ToString()).ToList(), min, 1, ChoiceCards(cards));
        return chosen.Count == 0 ? null : cards.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
    }

    private static async Task<List<CardInstance>> ChooseMany(EffectContext ctx, int player, string kind, string text,
        IReadOnlyList<CardInstance> cards, int max)
    {
        if (cards.Count == 0 || max <= 0) return new();
        var chosen = await ctx.Prompts.ChooseCards(player, kind, text,
            cards.Select(c => c.Id.ToString()).ToList(), 0, Math.Min(max, cards.Count), ChoiceCards(cards));
        return chosen.Select(id => cards.FirstOrDefault(c => c.Id.ToString() == id)).OfType<CardInstance>().ToList();
    }

    private static async Task<CardInstance?> ChooseOwnDiscard(EffectContext ctx, bool optional, bool asCost)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var picked = await Choose(ctx, ctx.OwnerIndex, "OwnHandDiscard", "选择1张手牌丢弃",
            me.Hand, optional ? 0 : 1);
        if (picked is null) return null;
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = asCost;
        try { AtomicOps.DiscardHand(me, picked); }
        finally { EffectRuntime.PayingCost = previous; }
        return picked;
    }

    private static async Task OpponentDiscard(EffectContext ctx, int count)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];
        for (int i = 0; i < count && opp.Hand.Count > 0; i++)
        {
            var picked = await Choose(ctx, oppIdx, "OwnHandDiscard", "选择1张手牌丢弃", opp.Hand, 1);
            if (picked is null) return;
            AtomicOps.DiscardHand(opp, picked);
        }
    }

    private static async Task<CardInstance?> LookTopToHand(EffectContext ctx, int count,
        Func<CardInstance, bool> filter, string text)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var top = me.Deck.Take(Math.Min(count, me.Deck.Count)).ToList();
        if (top.Count == 0) return null;
        var candidates = top.Where(filter).ToList();
        var answer = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal", text,
            candidates.Select(c => c.Id.ToString()).ToList(), 0, Math.Min(1, candidates.Count), ChoiceCards(top));
        var picked = answer.Count == 0 ? null : candidates.FirstOrDefault(c => c.Id.ToString() == answer[0]);
        if (picked is not null)
        {
            me.Deck.Remove(picked);
            me.Hand.Add(picked);
            ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new[] { picked.Info.Number });
        }
        var remaining = top.Where(c => !ReferenceEquals(c, picked)).ToList();
        var ordered = remaining;
        if (remaining.Count > 1)
        {
            var order = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReorderToBottom",
                "自选顺序放回卡组最下方（先选的牌在较上方）",
                remaining.Select(c => c.Id.ToString()).ToList(), remaining.Count, remaining.Count, ChoiceCards(remaining));
            var byId = remaining.ToDictionary(c => c.Id.ToString());
            ordered = order.Where(byId.ContainsKey).Select(id => byId[id]).Distinct().ToList();
            ordered.AddRange(remaining.Where(c => !ordered.Contains(c)));
        }
        foreach (var card in remaining) me.Deck.Remove(card);
        me.Deck.AddRange(ordered);
        return picked;
    }

    private static async Task PlayFromHand(EffectContext ctx, Func<CardInstance, bool> filter, string text)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var picked = await Choose(ctx, ctx.OwnerIndex, "OwnHandCharacter", text, me.Hand.Where(filter).ToList());
        if (picked is not null) await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
    }

    private static async Task PlayFromHandOrTrash(EffectContext ctx, Func<CardInstance, bool> filter, string text)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var cards = me.Hand.Where(filter).Select(c => (card: c, trash: false))
            .Concat(me.Trash.Where(filter).Select(c => (card: c, trash: true))).ToList();
        var picked = await Choose(ctx, ctx.OwnerIndex, "HandOrTrashCharacter", text,
            cards.Select(x => x.card).ToList());
        if (picked is null) return;
        if (cards.First(x => x.card.Id == picked.Id).trash) await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
        else await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
    }

    private static async Task KOChosen(EffectContext ctx, Func<CardInstance, bool> filter, string text)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var target = await Choose(ctx, ctx.OwnerIndex, "OpponentCharacter", text,
            ctx.State.Players[oppIdx].Characters.Where(filter).ToList());
        if (target is not null)
            await AtomicOps.KOByEffectAsync(ctx.State, oppIdx, target, ctx.Prompts, ctx.OwnerIndex);
    }

    private static void RegisterSelfKeyword(EffectContext ctx, string keyword,
        Func<GameState, int, CardInstance, bool> condition)
    {
        var id = ctx.Source.Id;
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true,
                Filter = c => c.Id == id },
            GrantKeyword = keyword,
            Predicate = (state, side, card) => !card.IsEffectsNullified && condition(state, side, card),
        });
    }

    private static void RegisterSelfCost(EffectContext ctx, int delta)
    {
        var id = ctx.Source.Id;
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true,
                Filter = c => c.Id == id },
            CostDelta = delta,
            Predicate = (_, _, card) => card.Id == id && !card.IsEffectsNullified,
        });
    }

    private static int AssignedDon(PlayerState player) => player.CostArea.Count(d => d.State == DonState.Attached);
    private static bool SlashLeader(PlayerState player)
        => player.Leader.HasProperty("斩");

    private static async Task ST31_001(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var id = ctx.Source.Id;
        RegisterSelfKeyword(ctx, "速攻", (_, _, card) => card.Id == id && me.AttachedDonCount(id) >= 2);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        await PlayFromHand(ctx, c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 5 &&
            c.Info.HasKeyword("草帽一伙") && !c.Info.NameIs("山智"),
            "将最多1张山智以外、费用不高于5的《草帽一伙》角色登场");
    }

    private static async Task ST31_002(EffectContext ctx)
    {
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        await PlayFromHand(ctx, c => c.Info.Kind is CardKind.Character or CardKind.Stage &&
            c.Info.Cost == 1 && c.Info.HasKeyword("草帽一伙"),
            "将最多1张费用为1的《草帽一伙》卡牌登场");
    }

    private static Task ST31_003(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var id = ctx.Source.Id;
        RegisterSelfKeyword(ctx, "阻挡者", (s, _, card) =>
            card.Id == id && s.CurrentTurnPlayer != ctx.OwnerIndex && AssignedDon(me) >= 3);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true,
                Filter = c => c.Id == id },
            PowerDelta = 3000,
            Predicate = (s, _, card) => card.Id == id && !card.IsEffectsNullified &&
                s.CurrentTurnPlayer != ctx.OwnerIndex && AssignedDon(me) >= 3,
        });
        return Task.CompletedTask;
    }

    private static async Task ST31_004(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var id = ctx.Source.Id;
        RegisterSelfKeyword(ctx, "速攻", (_, _, card) => card.Id == id && AssignedDon(me) >= 3);
        int strawHat = (me.Leader.Info.HasKeyword("草帽一伙") ? 1 : 0)
            + me.Characters.Count(c => c.Info.HasKeyword("草帽一伙"))
            + (me.StageCard?.Info.HasKeyword("草帽一伙") == true ? 1 : 0);
        if (strawHat == 0) return;
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var target = await Choose(ctx, ctx.OwnerIndex, "OpponentCharacter",
            $"选择对方最多1张角色，每有1张《草帽一伙》卡牌力量-1000（合计-{strawHat * 1000}）", opp.Characters);
        if (target is not null) AtomicOps.AddPowerThisTurn(target, -1000 * strawHat);
    }

    private static async Task ST31_005(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            await LookTopToHand(ctx, 5, c => c.Info.HasKeyword("草帽一伙"),
                "确认卡组最上方5张，将最多1张《草帽一伙》卡牌加入手牌");
            return;
        }
        if (ctx.Source.IsTapped || !me.CostArea.Any(d => d.State == DonState.Rest)) return;
        var targets = new List<CardInstance>();
        if (me.Leader.MatchesName("蒙奇·D·路飞")) targets.Add(me.Leader);
        targets.AddRange(me.Characters.Where(c => c.MatchesName("蒙奇·D·路飞")));
        if (targets.Count == 0) return;
        AtomicOps.RestCard(ctx.Source);
        var target = await Choose(ctx, ctx.OwnerIndex, "OwnLeaderOrCharacter", "赋予1张路飞最多1张休息咚!!", targets);
        if (target is not null) AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
    }

    private static async Task ST32_001(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var activeDon = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        var ids = new List<string>();
        if (!me.Leader.IsTapped && SlashLeader(me)) ids.Add(me.Leader.Id.ToString());
        ids.AddRange(activeDon.Select(d => d.Id.ToString()));
        if (ids.Count == 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["donChoices"] = activeDon.Select(d => new { id = d.Id.ToString(), state = "Active" }).ToList(),
        };
        var selected = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrDon",
            "可以将斩属性领袖或1张咚!!转为休息状态：抽2张，丢弃1张手牌", ids, 0, 1, extra);
        if (selected.Count == 0) return;
        if (selected[0] == me.Leader.Id.ToString()) me.Leader.IsTapped = true;
        else
        {
            var don = me.CostArea.FirstOrDefault(d => d.Id.ToString() == selected[0] && d.State == DonState.Active);
            if (don is null) return;
            don.State = DonState.Rest;
        }
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
        await ChooseOwnDiscard(ctx, optional: false, asCost: false);
    }

    private static async Task ST32_002(EffectContext ctx)
    {
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var target = await Choose(ctx, ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多1张原本费用不高于6的角色，直到下个对方回合结束时无法转为休息状态",
            opp.Characters.Where(c => c.Info.Cost <= 6).ToList());
        if (target is not null)
            AtomicOps.AddRestriction(target, RestrictionKind.CannotBeRested,
                KeywordDuration.UntilNextOpponentEndPhase, ctx.OwnerIndex);
    }

    private static async Task ST32_003(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnCharRested)
        {
            if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;
            if (!ctx.Vars.TryGetValue("restedCardId", out var value) || value?.ToString() != ctx.Source.Id.ToString()) return;
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            await ChooseOwnDiscard(ctx, optional: false, asCost: false);
            return;
        }
        if (!SlashLeader(me)) return;
        await PlayFromHand(ctx, c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 5 &&
            (c.Info.NameContains("佩罗娜") || c.HasProperty("斩")),
            "将最多1张费用不高于5的佩罗娜或斩属性角色登场");
    }

    private static Task ST32_004(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var id = ctx.Source.Id;
        RegisterSelfKeyword(ctx, "登场回合可攻击角色", (_, _, card) => card.Id == id && SlashLeader(me));
        return RestUpTo(ctx, 2, 2);
    }

    private static Task ST32_005(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var id = ctx.Source.Id;
        RegisterSelfKeyword(ctx, "登场回合可攻击角色", (_, _, card) => card.Id == id);
        return SlashLeader(me) ? RestUpTo(ctx, 1, 2) : Task.CompletedTask;
    }

    private static async Task RestUpTo(EffectContext ctx, int max, int maxCost)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var cards = ctx.State.Players[oppIdx].Characters
            .Where(c => ctx.State.CurrentCostOf(oppIdx, c) <= maxCost && !c.IsTapped).ToList();
        foreach (var card in await ChooseMany(ctx, ctx.OwnerIndex, "OpponentCharacter",
                     $"将对方最多{max}张费用不高于{maxCost}的角色转为休息状态", cards, max))
            AtomicOps.RestCard(card);
    }

    private static async Task ST33_001(EffectContext ctx)
    {
        if (await ChooseOwnDiscard(ctx, optional: true, asCost: true) is not null)
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }

    private static async Task ST33_002(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            await PlayFromHand(ctx, c => c.Info.Kind == CardKind.Character && c.Info.Cost <= 4 && c.Info.HasKeyword("海军"),
                "将最多1张费用不高于4的《海军》角色登场");
            return;
        }
        if (await ChooseOwnDiscard(ctx, optional: true, asCost: true) is not null &&
            ctx.State.Players[1 - ctx.OwnerIndex].Hand.Count >= 6)
            await OpponentDiscard(ctx, 1);
    }

    private static async Task ST33_003(EffectContext ctx)
    {
        if (await ChooseOwnDiscard(ctx, optional: true, asCost: true) is null) return;
        int oppIdx = 1 - ctx.OwnerIndex;
        var cards = ctx.State.Players[oppIdx].Characters
            .Where(c => ctx.State.CurrentCostOf(oppIdx, c) <= 2).ToList();
        var selected = await ChooseMany(ctx, ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多2张费用不高于2的角色放回卡组最下方", cards, 2);
        await AtomicOps.ProcessEffectLeavesAsync(ctx.State, oppIdx, selected, ctx.Prompts, "deck-bottom",
            AtomicOps.ReturnFieldToDeckBottom);
    }

    private static async Task ST33_005(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Leader.Info.HasKeyword("海军")) return;
        await PlayFromHand(ctx, c => c.Info.Kind == CardKind.Character && c.Info.Power <= 8000 &&
            c.Info.ColorList.Contains("蓝") && c.Info.HasKeyword("海军") && !c.Info.NameIs("蒙奇·D·戈普"),
            "将最多1张戈普以外、蓝色、力量不高于8000的《海军》角色登场");
    }

    private static async Task ST34_001(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            await PlayFromHand(ctx, c => c.Info.Kind == CardKind.Character && c.Info.Power <= 8000,
                "将最多1张力量不高于8000的角色登场");
            return;
        }
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex || !me.Leader.Info.HasKeyword("大妈海盗团")) return;
        if (!ctx.Vars.TryGetValue("owner", out var owner) || owner is not int ownerIdx || ownerIdx != ctx.OwnerIndex) return;
        string key = $"{ctx.Source.Id}-ST34-001-don";
        if (me.TurnOnceUsed.Contains(key)) return;
        if (me.DonDeck.Count == 0) return;
        var available = me.DonDeck.Take(2).ToList();
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DonDeck",
            "从咚!!卡组追加最多2张休息状态的咚!!",
            available.Select(d => d.Id.ToString()).ToList(), 0, available.Count);
        if (chosen.Count == 0) return;
        AtomicOps.RefreshDonFromDeck(me, Math.Min(chosen.Count, available.Count), DonState.Rest);
        me.TurnOnceUsed.Add(key);
    }

    private static async Task ST34_002(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Leader.Info.HasKeyword("大妈海盗团")) return;
        if (me.DonDeck.Count > 0 && await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "从咚!!卡组追加1张休息状态的咚!!？"))
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        int oppIdx = 1 - ctx.OwnerIndex;
        await KOChosen(ctx, c => ctx.State.CurrentCostOf(oppIdx, c) <= 2,
            "KO对方最多1张费用不高于2的角色");
    }

    private static Task ST34_003(EffectContext ctx) => LookTopToHand(ctx, 3,
        c => c.Info.HasKeyword("大妈海盗团"), "确认卡组最上方3张，将最多1张《大妈海盗团》卡牌加入手牌");

    private static async Task ST34_004(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.TotalDonInCostArea < 4 || me.Hand.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "咚!!-4并丢弃1张手牌，发动夏洛特·玲玲的登场效果？")) return;
        var selected = await Choose(ctx, ctx.OwnerIndex, "OwnHandDiscard", "选择1张手牌作为发动成本", me.Hand, 1);
        if (selected is null || !await AtomicOps.PromptReturnDonToDeck(ctx, 4)) return;
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        try { AtomicOps.DiscardHand(me, selected); }
        finally { EffectRuntime.PayingCost = previous; }
        if (me.Deck.Count > 0 && await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将卡组最上方1张卡牌加入生命区最上方？"))
            AtomicOps.AddLifeFromDeckTop(me, 1);
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var target = await Choose(ctx, ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多1张角色，本回合中原本力量变为0", opp.Characters);
        if (target is not null) target.OriginalPowerOverride = 0;
    }

    private static async Task ST34_005(EffectContext ctx)
    {
        if (ctx.State.Players[ctx.OwnerIndex].TotalDonInCostArea < 1) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        await KOChosen(ctx, c => c.Info.Power <= 2000, "KO对方最多1张原本力量不高于2000的角色");
    }

    private static Task ST35_001(EffectContext ctx) =>
        KOChosen(ctx, c => c.Info.Power <= 2000, "KO对方最多1张原本力量不高于2000的角色");

    private static async Task ST35_002(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var target = await Choose(ctx, ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多1张角色，本回合力量-3000", opp.Characters);
        if (target is not null) AtomicOps.AddPowerThisTurn(target, -3000);
    }

    private static async Task ST35_003(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Deck.Count < 2 || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将卡组最上方2张卡牌放入废弃区，发动乌鸦的攻击时效果？")) return;
        AtomicOps.MillTop(me, 2);
        if (ctx.State.Players[1 - ctx.OwnerIndex].Hand.Count >= 7) await OpponentDiscard(ctx, 1);
    }

    private static async Task ST35_004(EffectContext ctx)
    {
        RegisterSelfCost(ctx, 1);
        await RevolutionaryEnter(ctx);
    }

    private static async Task ST35_005(EffectContext ctx)
    {
        RegisterSelfCost(ctx, 3);
        await RevolutionaryEnter(ctx);
    }

    private static async Task RevolutionaryEnter(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var restDon = me.CostArea.Where(d => d.State == DonState.Rest).ToList();
        if (restDon.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnRestDon",
                "赋予我方领袖最多1张休息状态的咚!!",
                restDon.Select(d => d.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0) AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);
        }
        await PlayFromHandOrTrash(ctx, c => c.Info.Kind == CardKind.Character && c.Info.Power <= 4000 &&
            c.Info.HasKeyword("革命军"), "将手牌或废弃区中最多1张力量不高于4000的《革命军》角色登场");
    }
}

public sealed class ST31_001_Effect : ST31To35EffectBase { public override string CardNumber => "ST31-001"; }
public sealed class ST31_002_Effect : ST31To35EffectBase { public override string CardNumber => "ST31-002"; }
public sealed class ST31_003_Effect : ST31To35EffectBase { public override string CardNumber => "ST31-003"; }
public sealed class ST31_004_Effect : ST31To35EffectBase { public override string CardNumber => "ST31-004"; }
public sealed class ST31_005_Effect : ST31To35EffectBase { public override string CardNumber => "ST31-005"; }
public sealed class ST32_001_Effect : ST31To35EffectBase { public override string CardNumber => "ST32-001"; }
public sealed class ST32_002_Effect : ST31To35EffectBase { public override string CardNumber => "ST32-002"; }
public sealed class ST32_003_Effect : ST31To35EffectBase { public override string CardNumber => "ST32-003"; }
public sealed class ST32_004_Effect : ST31To35EffectBase { public override string CardNumber => "ST32-004"; }
public sealed class ST32_005_Effect : ST31To35EffectBase { public override string CardNumber => "ST32-005"; }
public sealed class ST33_001_Effect : ST31To35EffectBase { public override string CardNumber => "ST33-001"; }
public sealed class ST33_002_Effect : ST31To35EffectBase { public override string CardNumber => "ST33-002"; }
public sealed class ST33_003_Effect : ST31To35EffectBase { public override string CardNumber => "ST33-003"; }
public sealed class ST33_004_Effect : ST31To35EffectBase { public override string CardNumber => "ST33-004"; }
public sealed class ST33_005_Effect : ST31To35EffectBase { public override string CardNumber => "ST33-005"; }
public sealed class ST34_001_Effect : ST31To35EffectBase { public override string CardNumber => "ST34-001"; }
public sealed class ST34_002_Effect : ST31To35EffectBase { public override string CardNumber => "ST34-002"; }
public sealed class ST34_003_Effect : ST31To35EffectBase { public override string CardNumber => "ST34-003"; }
public sealed class ST34_004_Effect : ST31To35EffectBase { public override string CardNumber => "ST34-004"; }
public sealed class ST34_005_Effect : ST31To35EffectBase { public override string CardNumber => "ST34-005"; }
public sealed class ST35_001_Effect : ST31To35EffectBase { public override string CardNumber => "ST35-001"; }
public sealed class ST35_002_Effect : ST31To35EffectBase { public override string CardNumber => "ST35-002"; }
public sealed class ST35_003_Effect : ST31To35EffectBase { public override string CardNumber => "ST35-003"; }
public sealed class ST35_004_Effect : ST31To35EffectBase { public override string CardNumber => "ST35-004"; }
public sealed class ST35_005_Effect : ST31To35EffectBase { public override string CardNumber => "ST35-005"; }
