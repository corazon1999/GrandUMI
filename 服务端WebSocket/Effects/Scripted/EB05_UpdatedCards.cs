using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>EB05 新增卡牌的完整规则实现。共用选择器只负责交互与原子复验，不改变卡面语义。</summary>
internal static class EB05UpdatedEffects
{
    private static PlayerState Me(EffectContext ctx) => ctx.State.Players[ctx.OwnerIndex];
    private static PlayerState Opp(EffectContext ctx) => ctx.State.Players[1 - ctx.OwnerIndex];
    private static string OnceKey(CardInstance source, string effect) => $"{source.Info.Number}-{effect}:{source.Id}";

    private static Dictionary<string, object?> ChoiceCards(IEnumerable<CardInstance> cards) => new()
    {
        ["choiceCards"] = cards.Select(card => new
        {
            id = card.Id.ToString(),
            number = card.Info.Number,
        }).ToList(),
    };

    private static async Task<List<CardInstance>> Pick(
        EffectContext ctx,
        int chooser,
        string kind,
        string text,
        IEnumerable<CardInstance> source,
        int min,
        int max,
        IEnumerable<CardInstance>? display = null)
    {
        var cards = source.DistinctBy(card => card.Id).ToList();
        if (cards.Count == 0 || max <= 0) return new();
        max = Math.Min(max, cards.Count);
        min = Math.Min(min, max);
        var answer = await ctx.Prompts.ChooseCards(
            chooser,
            kind,
            text,
            cards.Select(card => card.Id.ToString()).ToList(),
            min,
            max,
            ChoiceCards(display ?? cards));
        var result = new List<CardInstance>();
        foreach (var id in answer)
        {
            var card = cards.FirstOrDefault(candidate => candidate.Id.ToString() == id);
            if (card is not null && result.All(candidate => candidate.Id != card.Id)) result.Add(card);
        }
        return result;
    }

    private static async Task<bool> DiscardOwn(
        EffectContext ctx,
        int count,
        Func<CardInstance, bool>? filter,
        string text,
        bool isCost)
    {
        var me = Me(ctx);
        var candidates = me.Hand.Where(card => filter?.Invoke(card) ?? true).ToList();
        if (candidates.Count < count) return false;
        var picked = await Pick(ctx, ctx.OwnerIndex, "OwnHandDiscard", text, candidates, count, count);
        if (picked.Count != count
            || picked.Any(card => !me.Hand.Contains(card) || !(filter?.Invoke(card) ?? true))) return false;
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = isCost;
        try
        {
            foreach (var card in picked) AtomicOps.DiscardHand(me, card);
        }
        finally
        {
            EffectRuntime.PayingCost = previous;
        }
        return true;
    }

    private static async Task<bool> RevealOwn(
        EffectContext ctx,
        int count,
        Func<CardInstance, bool> filter,
        string text)
    {
        var me = Me(ctx);
        var candidates = me.Hand.Where(filter).ToList();
        if (candidates.Count < count) return false;
        var picked = await Pick(ctx, ctx.OwnerIndex, "RevealOwnHand", text, candidates, 0, count);
        if (picked.Count != count || picked.Any(card => !me.Hand.Contains(card) || !filter(card))) return false;
        ctx.BroadcastReveal(picked);
        return true;
    }

    private static async Task<List<CardInstance>> SearchTop(
        EffectContext ctx,
        int count,
        Func<CardInstance, bool> filter,
        int maxPick,
        string text,
        bool trashRemainder)
    {
        var me = Me(ctx);
        var top = me.Deck.Take(count).ToList();
        if (top.Count == 0) return new();
        var candidates = top.Where(filter).ToList();
        var answer = await ctx.Prompts.ChooseCards(
            ctx.OwnerIndex,
            "LookTop",
            text,
            candidates.Select(card => card.Id.ToString()).ToList(),
            0,
            Math.Min(maxPick, candidates.Count),
            ChoiceCards(top));
        var picked = answer
            .Select(id => candidates.FirstOrDefault(card => card.Id.ToString() == id))
            .Where(card => card is not null)
            .Cast<CardInstance>()
            .DistinctBy(card => card.Id)
            .Take(maxPick)
            .ToList();
        var remainder = top.Where(card => picked.All(selected => selected.Id != card.Id)).ToList();
        List<CardInstance> orderedRemainder = remainder;
        if (!trashRemainder && remainder.Count > 1)
        {
            var order = await ctx.Prompts.ChooseCards(
                ctx.OwnerIndex,
                "ReorderToDeckBottom",
                "将剩余卡牌自选顺序放回卡组最下方（先选的牌在较上方）",
                remainder.Select(card => card.Id.ToString()).ToList(),
                0,
                remainder.Count,
                new Dictionary<string, object?>(ChoiceCards(remainder))
                {
                    ["allowDefaultOrder"] = true,
                });
            orderedRemainder = order
                .Select(id => remainder.FirstOrDefault(card => card.Id.ToString() == id))
                .Where(card => card is not null)
                .Cast<CardInstance>()
                .DistinctBy(card => card.Id)
                .ToList();
            orderedRemainder.AddRange(remainder.Where(card => orderedRemainder.All(item => item.Id != card.Id)));
        }

        // 两次 Prompt 后统一复验卡组顶，旧响应不得移走新的顶牌。
        if (me.Deck.Count < top.Count
            || !me.Deck.Take(top.Count).Select(card => card.Id).SequenceEqual(top.Select(card => card.Id))
            || picked.Any(card => !filter(card))) return new();
        me.Deck.RemoveRange(0, top.Count);
        foreach (var card in picked) me.Hand.Add(card);
        if (trashRemainder) me.Trash.AddRange(orderedRemainder);
        else me.Deck.AddRange(orderedRemainder);
        ctx.BroadcastReveal(picked);
        return picked;
    }

    private static async Task<bool> RestDonCost(EffectContext ctx, int count, string text)
    {
        var me = Me(ctx);
        var eligible = me.CostArea.Where(don => don.State == DonState.Active).ToList();
        if (eligible.Count < count) return false;
        var answer = await ctx.Prompts.ChooseCards(
            ctx.OwnerIndex,
            "RestOwnDon",
            text,
            eligible.Select(don => don.Id.ToString()).ToList(),
            0,
            count,
            new Dictionary<string, object?>
            {
                ["donChoices"] = eligible.Select(don => new { id = don.Id.ToString(), state = don.State.ToString() }).ToList(),
                ["canCancel"] = true,
            });
        if (answer.Count != count || answer.Distinct(StringComparer.Ordinal).Count() != count) return false;
        var selected = answer.Select(id => eligible.FirstOrDefault(don => don.Id.ToString() == id)).ToList();
        if (selected.Any(don => don is null || !me.CostArea.Contains(don) || don.State != DonState.Active)) return false;
        foreach (var don in selected.OfType<DonCard>()) don.State = DonState.Rest;
        return true;
    }

    private static async Task<int> AddDonFromDeckUpTo(
        EffectContext ctx,
        int maximum,
        DonState state,
        string text)
    {
        var me = Me(ctx);
        int capacity = Math.Max(0, ctx.State.MaxDonInCostAreaFor(ctx.OwnerIndex) - me.CostArea.Count);
        int limit = Math.Min(maximum, Math.Min(me.DonDeck.Count, capacity));
        if (limit <= 0) return 0;
        int count = await ctx.Prompts.ChooseOption(
            ctx.OwnerIndex,
            text,
            Enumerable.Range(0, limit + 1).Select(value => $"{value} 张").ToList());
        if (count < 0 || count > limit) return 0;
        return AtomicOps.RefreshDonFromDeck(me, count, state);
    }

    private static async Task<bool> DrawThenDiscard(EffectContext ctx, int draw, int discard)
    {
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, draw);
        if (ctx.State.IsGameOver) return false;
        return await DiscardOwn(ctx, discard, null, $"选择丢弃 {discard} 张手牌", isCost: false);
    }

    private static async Task<CardInstance?> ChooseOwnCharacter(
        EffectContext ctx,
        Func<CardInstance, bool> filter,
        string text,
        int min = 0)
        => (await Pick(ctx, ctx.OwnerIndex, "OwnCharacter", text,
            Me(ctx).Characters.Where(filter), min, 1)).FirstOrDefault();

    private static async Task<CardInstance?> ChooseOpponentCharacter(
        EffectContext ctx,
        Func<CardInstance, bool> filter,
        string text,
        int min = 0)
        => (await Pick(ctx, ctx.OwnerIndex, "OpponentCharacter", text,
            Opp(ctx).Characters.Where(filter), min, 1)).FirstOrDefault();

    private static bool TryReadGuid(IReadOnlyDictionary<string, object?>? payload, string key, out Guid id)
    {
        id = Guid.Empty;
        if (payload is null || !payload.TryGetValue(key, out var raw)) return false;
        var text = raw switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };
        return Guid.TryParse(text, out id);
    }

    private static bool TryReadInt(IReadOnlyDictionary<string, object?>? payload, string key, out int value)
    {
        value = default;
        if (payload is null || !payload.TryGetValue(key, out var raw)) return false;
        if (raw is int number)
        {
            value = number;
            return true;
        }
        return raw is JsonElement { ValueKind: JsonValueKind.Number } element
            && element.TryGetInt32(out value);
    }

    private static int ActingSideForLeave(
        GameState state,
        EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
    {
        if (TryReadInt(payload, "actingSide", out var actingSide)) return actingSide;
        if (trigger is EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd)
        {
            if (state.KOReason == "effect") return state.KOActingSide;
            if (state.CurrentBattle is { } battle) return battle.AttackerPlayerIndex;
        }
        return EffectRuntime.CurrentActingSide;
    }

    public static bool Handles(string number, EffectTrigger trigger) => number switch
    {
        "EB05-001" => trigger is EffectTrigger.OnEnterField or EffectTrigger.ActivatedMain,
        "EB05-002" => trigger == EffectTrigger.OnEnterField,
        "EB05-004" => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnKO,
        "EB05-005" => trigger is EffectTrigger.OnEnterField or EffectTrigger.ActivatedMain,
        "EB05-006" => trigger == EffectTrigger.ActivatedMain,
        "EB05-007" => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnMyTurnEnd,
        "EB05-009" => trigger is EffectTrigger.EventMain or EffectTrigger.EventCounter,
        "EB05-011" or "EB05-012" or "EB05-018" or "EB05-023"
            or "EB05-025" or "EB05-027" or "EB05-028" or "EB05-034" or "EB05-035"
            or "EB05-036" or "EB05-037" or "EB05-038" or "EB05-042" or "EB05-044"
            or "EB05-050" or "EB05-051" or "EB05-056" => trigger == EffectTrigger.OnEnterField,
        "EB05-013" or "EB05-017" or "EB05-046" => trigger == EffectTrigger.OnOppAttackDeclare,
        "EB05-014" => trigger is EffectTrigger.OnEnterField or EffectTrigger.ActivatedMain,
        "EB05-020" => trigger == EffectTrigger.EventMain,
        "EB05-021" => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnAttackDeclare,
        "EB05-024" or "EB05-047" => trigger == EffectTrigger.OnEnterField,
        "EB05-029" => trigger is EffectTrigger.EventMain or EffectTrigger.OnLifeRevealTrigger,
        "EB05-031" => trigger is EffectTrigger.OnEnterField or EffectTrigger.ActivatedMain,
        "EB05-039" or "EB05-048" or "EB05-060" => trigger is EffectTrigger.EventMain or EffectTrigger.EventCounter,
        "EB05-022" or "EB05-043" => trigger == EffectTrigger.OnKO,
        "EB05-045" => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnKO,
        "EB05-052" => trigger is EffectTrigger.PreDamageToLeader or EffectTrigger.OnLifeRevealTrigger,
        "EB05-053" => trigger is EffectTrigger.OnAttackDeclare or EffectTrigger.OnLifeRevealTrigger,
        "EB05-054" => trigger == EffectTrigger.OnLifeRevealTrigger,
        "EB05-055" => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnLifeRevealTrigger,
        "EB05-057" => trigger is EffectTrigger.ActivatedMain or EffectTrigger.OnLifeRevealTrigger,
        "EB05-061" => trigger is EffectTrigger.OnEnterField or EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd
            or EffectTrigger.OnAllyWillLeaveField,
        _ => false,
    };

    public static string? GetActivatedMainUnavailableReason(
        string number,
        GameState state,
        int owner,
        CardInstance source)
    {
        var me = state.Players[owner];
        if (Handles(number, EffectTrigger.ActivatedMain) && state.CurrentTurnPlayer != owner)
            return "只能在自己的回合发动";
        return number switch
        {
            "EB05-001" when me.TurnOnceUsed.Contains(OnceKey(source, "main")) => "本回合已经发动过此效果",
            "EB05-005" when !me.Characters.Contains(source) => "此角色不在角色区",
            "EB05-006" when me.TurnOnceUsed.Contains(OnceKey(source, "main")) => "本回合已经发动过此效果",
            "EB05-006" when me.LifeArea.Count == 0 => "生命区没有可加入手牌的卡牌",
            "EB05-006" when state.NoEffectLifeToHandThisTurn.Contains(owner) => "本回合不能通过效果将生命卡加入手牌",
            "EB05-014" when !me.Characters.Contains(source) || source.IsTapped || !AtomicOps.CanRestCard(state, source)
                => "此角色必须能够转为休息状态",
            "EB05-031" when !me.Characters.Contains(source) => "此角色不在角色区",
            "EB05-031" when !state.Players[1 - owner].Characters.Any(card => state.OriginalPowerOf(1 - owner, card) >= 8000)
                => "对方场上需要原本力量不低于8000的角色",
            "EB05-057" when !me.Characters.Contains(source) => "此角色不在角色区",
            "EB05-057" when me.TurnOnceUsed.Contains(OnceKey(source, "main")) => "本回合已经发动过此效果",
            "EB05-057" when !new[] { me.Leader }.Concat(me.Characters).Any(card => card.HasProperty("特") || card.HasProperty("知"))
                => "需要拥有属性（特）或（知）的领袖或角色",
            _ => null,
        };
    }

    public static bool IsTriggerAvailable(
        string number,
        GameState state,
        int owner,
        CardInstance source,
        EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
    {
        var me = state.Players[owner];
        return number switch
        {
            "EB05-013" when trigger == EffectTrigger.OnOppAttackDeclare
                => !me.TurnOnceUsed.Contains(OnceKey(source, "attack"))
                    && me.CostArea.Any(don => don.State == DonState.Active)
                    && (me.Leader.Info.HasKeyword("鱼人族") || me.Leader.Info.HasKeyword("人鱼族")),
            "EB05-017" when trigger == EffectTrigger.OnOppAttackDeclare
                => me.Characters.Contains(source)
                    && me.CostArea.Any(don => don.State == DonState.Active)
                    && (me.Leader.Info.HasKeyword("鱼人族") || me.Leader.Info.HasKeyword("人鱼族")),
            "EB05-046" when trigger == EffectTrigger.OnOppAttackDeclare
                => me.Hand.Count > 0 && me.Trash.Count >= 9,
            "EB05-052" when trigger == EffectTrigger.PreDamageToLeader
                => me.Characters.Contains(source),
            "EB05-053" when trigger == EffectTrigger.OnAttackDeclare
                => !me.TurnOnceUsed.Contains(OnceKey(source, "attack"))
                    && state.Players[1 - owner].Hand.Count >= 9,
            "EB05-061" when trigger is EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField
                => IsNamiReplacementAvailable(state, owner, source, trigger, payload),
            _ => true,
        };
    }

    private static bool IsNamiReplacementAvailable(
        GameState state,
        int owner,
        CardInstance source,
        EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
    {
        var me = state.Players[owner];
        if (!me.Characters.Contains(source)
            || me.LifeArea.Count == 0
            || state.NoEffectLifeToHandThisTurn.Contains(owner)
            || me.TurnOnceUsed.Contains(OnceKey(source, "leave"))
            || ActingSideForLeave(state, trigger, payload) != 1 - owner) return false;
        Guid victimId;
        if (trigger == EffectTrigger.PreKO) victimId = source.Id;
        else if (!TryReadGuid(payload, "victimId", out victimId)) return false;
        var victim = me.Characters.FirstOrDefault(card => card.Id == victimId);
        return victim is not null && state.OriginalPowerOf(owner, victim) <= 6000;
    }

    public static Task RegisterFieldStatic(string number, EffectContext ctx)
    {
        if (number is not ("EB05-024" or "EB05-046" or "EB05-047")) return Task.CompletedTask;
        string id = ctx.Source.Id.ToString();
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == id);
        switch (number)
        {
            case "EB05-024":
                ctx.State.ContinuousEffects.Add(new ContinuousEffect
                {
                    SourceCardId = id,
                    Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
                    GrantKeyword = "阻挡者",
                    Predicate = (state, side, card) => side == owner && card.Id == ctx.Source.Id
                        && state.Players[owner].Leader.Info.HasKeyword("因佩尔地狱"),
                });
                ctx.State.ContinuousEffects.Add(new ContinuousEffect
                {
                    SourceCardId = id,
                    Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
                    CostDelta = 2,
                    Predicate = (state, side, card) => side == owner && card.Id == ctx.Source.Id
                        && state.Players[owner].Leader.Info.HasKeyword("因佩尔地狱"),
                });
                break;
            case "EB05-046":
                bool HasBigOpponent(GameState state) => state.Players[1 - owner].Characters
                    .Any(card => state.OriginalPowerOf(1 - owner, card) >= 8000);
                ctx.State.ContinuousEffects.Add(new ContinuousEffect
                {
                    SourceCardId = id,
                    Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
                    GrantKeyword = "阻挡者",
                    Predicate = (state, side, card) => side == owner && card.Id == ctx.Source.Id && HasBigOpponent(state),
                });
                ctx.State.ContinuousEffects.Add(new ContinuousEffect
                {
                    SourceCardId = id,
                    Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
                    PowerDelta = 3000,
                    Predicate = (state, side, card) => side == owner && card.Id == ctx.Source.Id && HasBigOpponent(state),
                });
                break;
            case "EB05-047":
                ctx.State.ContinuousEffects.Add(new ContinuousEffect
                {
                    SourceCardId = id,
                    Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
                    CostDelta = 12,
                    Predicate = (_, side, card) => side == owner && card.Id == ctx.Source.Id,
                });
                break;
        }
        return Task.CompletedTask;
    }

    public static async Task Resolve(string number, EffectContext ctx)
    {
        switch (number)
        {
            case "EB05-001": await C001(ctx); break;
            case "EB05-002": await C002(ctx); break;
            case "EB05-004": await C004(ctx); break;
            case "EB05-005": await C005(ctx); break;
            case "EB05-006": await C006(ctx); break;
            case "EB05-007": await C007(ctx); break;
            case "EB05-009": await C009(ctx); break;
            case "EB05-011": await C011(ctx); break;
            case "EB05-012": await C012(ctx); break;
            case "EB05-013": await C013(ctx); break;
            case "EB05-014": await C014(ctx); break;
            case "EB05-017": await C017(ctx); break;
            case "EB05-018": await C018(ctx); break;
            case "EB05-020": await C020(ctx); break;
            case "EB05-021": await C021(ctx); break;
            case "EB05-022": await C022(ctx); break;
            case "EB05-023": await C023(ctx); break;
            case "EB05-025": await C025(ctx); break;
            case "EB05-027": await C027(ctx); break;
            case "EB05-028": await C028(ctx); break;
            case "EB05-029": await C029(ctx); break;
            case "EB05-031": await C031(ctx); break;
            case "EB05-034": await C034(ctx); break;
            case "EB05-035": await C035(ctx); break;
            case "EB05-036": await C036(ctx); break;
            case "EB05-037": await C037(ctx); break;
            case "EB05-038": await C038(ctx); break;
            case "EB05-039": await C039(ctx); break;
            case "EB05-042": await C042(ctx); break;
            case "EB05-043": await C043(ctx); break;
            case "EB05-044": await C044(ctx); break;
            case "EB05-045": await C045(ctx); break;
            case "EB05-046": await C046(ctx); break;
            case "EB05-047": await C047(ctx); break;
            case "EB05-048": await C048(ctx); break;
            case "EB05-050": await C050(ctx); break;
            case "EB05-051": await C051(ctx); break;
            case "EB05-052": await C052(ctx); break;
            case "EB05-053": await C053(ctx); break;
            case "EB05-054": await C054(ctx); break;
            case "EB05-055": await C055(ctx); break;
            case "EB05-056": await C056(ctx); break;
            case "EB05-057": await C057(ctx); break;
            case "EB05-060": await C060(ctx); break;
            case "EB05-061": await C061(ctx); break;
        }
    }

    private static async Task C001(EffectContext ctx)
    {
        var me = Me(ctx);
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (!me.Leader.Info.HasKeyword("超新星")) return;
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);
            if (ctx.State.IsGameOver) return;
            var picked = await Pick(ctx, ctx.OwnerIndex, "OwnHandCharacter",
                "将手牌中最多2张力量不高于2000的角色登场",
                me.Hand.Where(card => card.Info.Kind == CardKind.Character && card.Info.Power <= 2000), 0, 2);
            foreach (var card in picked)
                if (me.Hand.Contains(card)) await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
            return;
        }
        if (ctx.Trigger != EffectTrigger.ActivatedMain
            || GetActivatedMainUnavailableReason("EB05-001", ctx.State, ctx.OwnerIndex, ctx.Source) is not null) return;
        string key = OnceKey(ctx.Source, "main");
        var opponent = Opp(ctx);
        var candidates = new[] { opponent.Leader }.Concat(opponent.Characters)
            .Where(card => ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, card) >= 6000);
        var target = (await Pick(ctx, ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "选择对方最多1张当前力量不低于6000的领袖或角色，力量-1000",
            candidates, 0, 1)).FirstOrDefault();
        if (target is not null && (ReferenceEquals(opponent.Leader, target) || opponent.Characters.Contains(target))
            && ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, target) >= 6000)
            AtomicOps.AddPowerUntilOppEnd(target, -1000, ctx.OwnerIndex);
        me.TurnOnceUsed.Add(key);
    }

    private static async Task C002(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        await SearchTop(ctx, 5,
            card => card.Info.Cost >= 2 && card.Info.HasKeyword("海军"), 2,
            "确认卡组顶5张，公开最多2张费用不低于2的《海军》卡牌加入手牌",
            trashRemainder: false);
        await DiscardOwn(ctx, 1, null, "之后，选择丢弃1张手牌", isCost: false);
    }

    private static async Task C004(EffectContext ctx)
    {
        var me = Me(ctx);
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (await RevealOwn(ctx, 2, card => card.Info.Kind == CardKind.Event,
                    "可以公开2张事件卡牌以抽取2张卡牌"))
                await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);
            return;
        }
        if (ctx.Trigger != EffectTrigger.OnKO) return;
        var picked = await Pick(ctx, ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多1张力量不高于6000的角色以休息状态登场",
            me.Hand.Where(card => card.Info.Kind == CardKind.Character && card.Info.Power <= 6000), 0, 1);
        if (picked.Count == 1 && me.Hand.Contains(picked[0]))
            await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked[0], restState: true);
    }

    private static async Task C005(EffectContext ctx)
    {
        var me = Me(ctx);
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var picked = await Pick(ctx, ctx.OwnerIndex, "OwnCharacter",
                "选择最多3张同时拥有《革命军》特征和【触发】的角色，力量+2000",
                me.Characters.Where(card => card.Info.HasKeyword("革命军") && !string.IsNullOrEmpty(card.Info.Trigger)), 0, 3);
            foreach (var card in picked.Where(me.Characters.Contains)) AtomicOps.AddPowerThisTurn(card, 2000);
            return;
        }
        if (ctx.Trigger != EffectTrigger.ActivatedMain || !me.Characters.Contains(ctx.Source)) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将此角色放置到废弃区，使对方最多1张角色本回合力量-2000？")) return;
        if (!me.Characters.Contains(ctx.Source)) return;
        AtomicOps.TrashFieldCard(ctx.State, ctx.OwnerIndex, ctx.Source, ignoreEffectLeaveGuard: true);
        if (!me.Trash.Contains(ctx.Source)) return;
        var target = await ChooseOpponentCharacter(ctx, _ => true, "选择对方最多1张角色，力量-2000");
        if (target is not null && Opp(ctx).Characters.Contains(target)) AtomicOps.AddPowerThisTurn(target, -2000);
    }

    private static async Task C006(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.ActivatedMain
            || GetActivatedMainUnavailableReason("EB05-006", ctx.State, ctx.OwnerIndex, ctx.Source) is not null) return;
        var me = Me(ctx);
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将生命区最上方1张卡牌加入手牌，选择一项力量修正？")) return;
        if (me.LifeArea.Count == 0
            || ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)
            || me.TurnOnceUsed.Contains(OnceKey(ctx.Source, "main"))) return;
        var life = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        life.IsLifeFaceUp = false;
        me.Hand.Add(life);
        me.TurnOnceUsed.Add(OnceKey(ctx.Source, "main"));
        int option = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择一项效果",
            ["最多1张角色力量+3000", "最多1张角色力量-4000"]);
        var allCharacters = ctx.State.Players.SelectMany(player => player.Characters).ToList();
        var target = (await Pick(ctx, ctx.OwnerIndex, "AnyCharacter",
            option == 1 ? "选择最多1张角色，力量-4000" : "选择最多1张角色，力量+3000",
            allCharacters, 0, 1)).FirstOrDefault();
        if (target is null || ctx.State.SideOf(target) < 0) return;
        AtomicOps.AddPowerThisTurn(target, option == 1 ? -4000 : 3000);
    }

    private static async Task C007(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (await RevealOwn(ctx, 3,
                    card => card.Info.Kind == CardKind.Event || card.Info.HasKeyword("班克禁区"),
                    "可以公开合计3张事件卡牌或《班克禁区》卡牌以抽取1张卡牌"))
                await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
            return;
        }
        if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
            AtomicOps.AddPowerUntilOppEnd(ctx.Source, 5000, ctx.OwnerIndex);
    }

    private static Task C009(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventCounter)
            AtomicOps.AddPowerThisBattle(Me(ctx).Leader, 3000);
        else if (ctx.Trigger == EffectTrigger.EventMain)
            foreach (var card in Me(ctx).Characters.Where(card => ctx.State.OriginalPowerOf(ctx.OwnerIndex, card) <= 4000))
                AtomicOps.AddPowerUntilOppEnd(card, 1000, ctx.OwnerIndex);
        return Task.CompletedTask;
    }

    private static async Task C011(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField || !Me(ctx).Leader.MatchesName("白星")) return;
        var me = Me(ctx);
        if (me.LifeArea.Count == 0 || !me.LifeArea[0].IsLifeFaceUp
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将生命区最上方卡牌翻至背面，休息对方最多1张费用不高于5的角色？")) return;
        if (me.LifeArea.Count == 0 || !me.LifeArea[0].IsLifeFaceUp) return;
        me.LifeArea[0].IsLifeFaceUp = false;
        var target = await ChooseOpponentCharacter(ctx,
            card => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, card) <= 5,
            "选择对方最多1张费用不高于5的角色转为休息状态");
        if (target is not null && Opp(ctx).Characters.Contains(target)
            && ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, target) <= 5)
            AtomicOps.RestCard(ctx.State, target);
    }

    private static async Task C012(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        var target = await ChooseOpponentCharacter(ctx,
            card => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, card) <= 6,
            "选择对方最多1张费用不高于6的角色转为休息状态");
        if (target is not null && Opp(ctx).Characters.Contains(target)
            && ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, target) <= 6)
            AtomicOps.RestCard(ctx.State, target);
    }

    private static async Task C013(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnOppAttackDeclare
            || !IsTriggerAvailable("EB05-013", ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)
            || !await RestDonCost(ctx, 1, "选择1张活跃咚!!转为休息状态，或取消发动")) return;
        Me(ctx).TurnOnceUsed.Add(OnceKey(ctx.Source, "attack"));
        AtomicOps.AddPowerThisBattle(Me(ctx).Leader, 2000);
    }

    private static async Task C014(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            await SearchTop(ctx, 5,
                card => card.MatchesName("梅迦罗") || card.Info.HasKeyword("海王类"), 2,
                "确认卡组顶5张，公开合计最多2张“梅迦罗”或《海王类》卡牌加入手牌",
                trashRemainder: true);
            return;
        }
        if (ctx.Trigger != EffectTrigger.ActivatedMain
            || GetActivatedMainUnavailableReason("EB05-014", ctx.State, ctx.OwnerIndex, ctx.Source) is not null
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将此角色转为休息状态，使1张《海王类》角色获得速攻？")) return;
        if (!Me(ctx).Characters.Contains(ctx.Source) || ctx.Source.IsTapped
            || !AtomicOps.CanRestCard(ctx.State, ctx.Source)) return;
        AtomicOps.RestCard(ctx.Source);
        var target = await ChooseOwnCharacter(ctx,
            card => card.Info.HasKeyword("海王类") && ctx.State.CurrentPowerOf(ctx.OwnerIndex, card) <= 6000,
            "选择最多1张力量不高于6000的《海王类》角色获得速攻");
        if (target is not null && Me(ctx).Characters.Contains(target)
            && target.Info.HasKeyword("海王类") && ctx.State.CurrentPowerOf(ctx.OwnerIndex, target) <= 6000)
            AtomicOps.GiveKeyword(target, "速攻", KeywordDuration.ThisTurn, ctx.OwnerIndex);
    }

    private static async Task C017(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnOppAttackDeclare
            || !IsTriggerAvailable("EB05-017", ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "休息1张咚!!并将此角色放置到废弃区，使领袖本次战斗力量+4000？")) return;
        var me = Me(ctx);
        var active = me.CostArea.Where(don => don.State == DonState.Active).ToList();
        if (active.Count == 0 || !me.Characters.Contains(ctx.Source)) return;
        var answer = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RestOwnDon", "选择转为休息状态的咚!!",
            active.Select(don => don.Id.ToString()).ToList(), 1, 1,
            new Dictionary<string, object?>
            {
                ["donChoices"] = active.Select(don => new { id = don.Id.ToString(), state = don.State.ToString() }).ToList(),
            });
        var selected = active.FirstOrDefault(don => answer.Count == 1 && don.Id.ToString() == answer[0]);
        if (selected is null || !me.CostArea.Contains(selected) || selected.State != DonState.Active
            || !me.Characters.Contains(ctx.Source)) return;
        selected.State = DonState.Rest;
        AtomicOps.TrashFieldCard(ctx.State, ctx.OwnerIndex, ctx.Source, ignoreEffectLeaveGuard: true);
        if (!me.Trash.Contains(ctx.Source))
        {
            if (selected.State == DonState.Rest) selected.State = DonState.Active;
            return;
        }
        AtomicOps.AddPowerThisBattle(me.Leader, 4000);
    }

    private static async Task C018(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        var target = await ChooseOpponentCharacter(ctx,
            card => card.IsTapped && ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, card) <= 6,
            "选择对方最多1张休息状态且费用不高于6的角色，下个重置阶段无法转为活跃状态");
        if (target is not null && Opp(ctx).Characters.Contains(target) && target.IsTapped
            && ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, target) <= 6)
            AtomicOps.PreventActivateNextReset(target);
    }

    private static async Task C020(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.EventMain) return;
        var leader = Me(ctx).Leader;
        if (!leader.MatchesName("白星") || leader.IsTapped || !AtomicOps.CanRestCard(ctx.State, leader)
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将领袖“白星”转为休息状态，抽取2张卡牌？")) return;
        if (leader.IsTapped || !AtomicOps.CanRestCard(ctx.State, leader)) return;
        AtomicOps.RestCard(leader);
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);
    }

    private static async Task C021(EffectContext ctx)
    {
        var me = Me(ctx);
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);
            if (ctx.State.IsGameOver) return;
            var picked = await Pick(ctx, ctx.OwnerIndex, "OwnHandCharacter",
                "将手牌中最多1张费用不高于8的《十字公会》角色登场",
                me.Hand.Where(card => card.Info.Kind == CardKind.Character
                    && card.Info.Cost <= 8 && card.Info.HasKeyword("十字公会")), 0, 1);
            if (picked.Count == 1 && me.Hand.Contains(picked[0]))
                await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked[0]);
            // 禁止标记必须在被效果登场卡的延迟【登场时】结算前建立。
            ctx.State.NoPlayCharacterThisTurn.Add(ctx.OwnerIndex);
            return;
        }
        if (ctx.Trigger != EffectTrigger.OnAttackDeclare) return;
        var target = await ChooseOpponentCharacter(ctx, _ => true, "选择对方最多1张角色，本回合效果无效");
        if (target is not null && Opp(ctx).Characters.Contains(target))
            AtomicOps.NullifyEffects(target, KeywordDuration.ThisTurn);
    }

    private static async Task C022(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnKO) await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);
    }

    private static async Task C023(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        if (Me(ctx).Hand.Count(card => card.Info.Kind == CardKind.Event) >= 2
            && await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "丢弃2张事件卡牌以抽取3张卡牌？")
            && await DiscardOwn(ctx, 2, card => card.Info.Kind == CardKind.Event,
                "可以丢弃2张事件卡牌以抽取3张卡牌", isCost: true))
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 3);
    }

    private static async Task C025(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        await SearchTop(ctx, 3, card => card.Info.HasKeyword("因佩尔地狱"), 1,
            "确认卡组顶3张，公开最多1张《因佩尔地狱》卡牌加入手牌", trashRemainder: false);
    }

    private static async Task C027(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        if (!await DrawThenDiscard(ctx, 3, 2)) return;
        var targets = ctx.State.Players.SelectMany((player, side) => player.Characters
            .Where(card => ctx.State.CurrentCostOf(side, card) <= 2)
            .Select(card => (side, card))).ToList();
        var picked = (await Pick(ctx, ctx.OwnerIndex, "AnyCharacter",
            "选择最多1张费用不高于2的角色放回其持有者卡组最下方",
            targets.Select(item => item.card), 0, 1)).FirstOrDefault();
        if (picked is null) return;
        var selected = targets.FirstOrDefault(item => item.card.Id == picked.Id);
        if (selected.card is null || !ctx.State.Players[selected.side].Characters.Contains(selected.card)
            || ctx.State.CurrentCostOf(selected.side, selected.card) > 2) return;
        if (!await AtomicOps.TryEffectLeaveGuard(ctx.State, selected.side, selected.card,
                ctx.Prompts, "deck-bottom"))
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, selected.side, selected.card);
    }

    private static async Task C028(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField || Opp(ctx).Hand.Count < 9) return;
        await AtomicOps.OpponentDiscardChosen(ctx.State, ctx.Prompts, 1 - ctx.OwnerIndex, 4);
    }

    private static async Task C029(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            await DrawThenDiscard(ctx, 2, 1);
            return;
        }
        if (ctx.Trigger != EffectTrigger.EventMain || Me(ctx).Hand.Count == 0
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "丢弃1张手牌，使对方1张费用不高于6的角色效果无效并放回手牌？")) return;
        if (!await DiscardOwn(ctx, 1, null, "选择作为成本丢弃的手牌", isCost: true)) return;
        var target = await ChooseOpponentCharacter(ctx,
            card => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, card) <= 6,
            "选择对方最多1张费用不高于6的角色");
        if (target is null || !Opp(ctx).Characters.Contains(target)
            || ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, target) > 6) return;
        AtomicOps.NullifyEffects(target, KeywordDuration.ThisTurn);
        if (!await AtomicOps.TryEffectLeaveGuard(ctx.State, 1 - ctx.OwnerIndex, target, ctx.Prompts, "hand"))
            AtomicOps.BounceToHand(ctx.State, 1 - ctx.OwnerIndex, target);
    }

    private static async Task C031(EffectContext ctx)
    {
        var me = Me(ctx);
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (!me.Leader.Info.HasKeyword("温思默克家")
                || !await AtomicOps.PromptReturnDonToDeck(ctx, ctx.OwnerIndex, 1, optional: true)) return;
            AtomicOps.MillTop(me, 10);
            return;
        }
        if (ctx.Trigger != EffectTrigger.ActivatedMain
            || GetActivatedMainUnavailableReason("EB05-031", ctx.State, ctx.OwnerIndex, ctx.Source) is not null
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将此角色放置到废弃区，从咚!!卡组追加最多4张活跃咚!!？")) return;
        if (!me.Characters.Contains(ctx.Source)
            || !Opp(ctx).Characters.Any(card => ctx.State.OriginalPowerOf(1 - ctx.OwnerIndex, card) >= 8000)) return;
        AtomicOps.TrashFieldCard(ctx.State, ctx.OwnerIndex, ctx.Source, ignoreEffectLeaveGuard: true);
        if (me.Trash.Contains(ctx.Source))
            await AddDonFromDeckUpTo(ctx, 4, DonState.Active, "选择追加的活跃咚!!张数");
    }

    private static async Task C034(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField
            || !await AtomicOps.PromptReturnDonToDeck(ctx, ctx.OwnerIndex, 2, optional: true)) return;
        if (Me(ctx).CostArea.Count < 7) return;
        var target = await ChooseOpponentCharacter(ctx, _ => true, "选择对方最多1张角色，本回合力量-4000");
        if (target is not null && Opp(ctx).Characters.Contains(target)) AtomicOps.AddPowerThisTurn(target, -4000);
    }

    private static async Task C035(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        var me = Me(ctx);
        if (!me.Leader.Info.HasKeyword("草帽一伙")
            || Opp(ctx).CostArea.Count - me.CostArea.Count < 6) return;
        if (!await DrawThenDiscard(ctx, 3, 2)) return;
        await AddDonFromDeckUpTo(ctx, 4, DonState.Rest, "选择追加的休息咚!!张数");
    }

    private static async Task C036(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField || !Me(ctx).Leader.Info.HasKeyword("海军")) return;
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        if (!ctx.State.IsGameOver)
            await AddDonFromDeckUpTo(ctx, 1, DonState.Rest, "选择是否追加1张休息咚!!");
    }

    private static async Task C037(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        await SearchTop(ctx, 3, card => card.Info.HasKeyword("百兽海盗团"), 1,
            "确认卡组顶3张，公开最多1张《百兽海盗团》卡牌加入手牌", trashRemainder: false);
    }

    private static async Task C038(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        var target = await ChooseOwnCharacter(ctx, card => card.MatchesName("赛诺尔·平克"),
            "选择我方最多1张“赛诺尔·平克”，本回合力量+3000");
        if (target is not null && Me(ctx).Characters.Contains(target) && target.MatchesName("赛诺尔·平克"))
            AtomicOps.AddPowerThisTurn(target, 3000);
    }

    private static async Task C039(EffectContext ctx)
    {
        if (ctx.Trigger is not (EffectTrigger.EventMain or EffectTrigger.EventCounter)) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, ctx.OwnerIndex, 1, optional: true)) return;
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            AtomicOps.AddPowerThisBattle(Me(ctx).Leader, 4000);
            return;
        }
        if (!Me(ctx).Leader.Info.HasKeyword("温思默克家")) return;
        var target = await ChooseOpponentCharacter(ctx, _ => true, "选择对方最多1张角色，本回合力量-4000");
        if (target is not null && Opp(ctx).Characters.Contains(target)) AtomicOps.AddPowerThisTurn(target, -4000);
    }

    private static async Task C042(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField) await DrawThenDiscard(ctx, 2, 2);
    }

    private static async Task C043(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnKO) return;
        var target = await ChooseOpponentCharacter(ctx,
            card => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, card) <= 6,
            "选择对方最多1张费用不高于6的角色");
        if (target is null || !Opp(ctx).Characters.Contains(target)
            || ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, target) > 6) return;
        var options = AtomicOps.CanRestCard(ctx.State, target)
            ? new[] { "KO该角色", "将该角色转为休息状态" }
            : new[] { "KO该角色" };
        int option = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择处理方式", options);
        if (option == 1 && options.Length > 1) AtomicOps.RestCard(ctx.State, target);
        else await AtomicOps.KOByEffectAsync(ctx.State, 1 - ctx.OwnerIndex, target, ctx.Prompts, ctx.OwnerIndex);
    }

    private static Task C044(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField
            && Me(ctx).Leader.Info.HasKeywordContaining("巴洛克工作室")
            && Opp(ctx).Characters.Any(card => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, card) == 0))
            AtomicOps.AddPowerThisTurn(Opp(ctx).Leader, -1000);
        return Task.CompletedTask;
    }

    private static async Task C045(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var target = await ChooseOwnCharacter(ctx,
                card => card.Info.HasKeywordContaining("巴洛克工作室"),
                "选择我方最多1张《巴洛克工作室》角色KO");
            if (target is not null && Me(ctx).Characters.Contains(target)
                && target.Info.HasKeywordContaining("巴洛克工作室"))
                await AtomicOps.KOByEffectAsync(ctx.State, ctx.OwnerIndex, target, ctx.Prompts, ctx.OwnerIndex);
            return;
        }
        if (ctx.Trigger == EffectTrigger.OnKO)
            await SearchTop(ctx, 3, card => card.Info.HasKeywordContaining("巴洛克工作室"), 1,
                "确认卡组顶3张，公开最多1张《巴洛克工作室》卡牌加入手牌", trashRemainder: true);
    }

    private static async Task C046(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnOppAttackDeclare
            || !IsTriggerAvailable("EB05-046", ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "丢弃1张手牌，使我方领袖本回合原本力量变为7000？")) return;
        if (!await DiscardOwn(ctx, 1, null, "选择作为成本丢弃的手牌", isCost: true)
            || Me(ctx).Trash.Count < 10) return;
        Me(ctx).Leader.OriginalPowerOverride = 7000;
    }

    private static async Task C047(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField || Me(ctx).Hand.Count == 0
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "丢弃1张手牌，将废弃区最多1张费用不高于2的角色登场？")) return;
        if (!await DiscardOwn(ctx, 1, null, "选择作为成本丢弃的手牌", isCost: true)) return;
        var me = Me(ctx);
        var picked = await Pick(ctx, ctx.OwnerIndex, "OwnTrashCharacter",
            "将废弃区最多1张费用不高于2的角色登场",
            me.Trash.Where(card => card.Info.Kind == CardKind.Character && card.Info.Cost <= 2), 0, 1);
        if (picked.Count == 1 && me.Trash.Contains(picked[0]) && picked[0].Info.Cost <= 2)
            await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked[0]);
    }

    private static async Task C048(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            AtomicOps.AddPowerThisBattle(Me(ctx).Leader, 3000);
            return;
        }
        if (ctx.Trigger != EffectTrigger.EventMain) return;
        var me = Me(ctx);
        var active = me.CostArea.Where(don => don.State == DonState.Active).ToList();
        var costs = me.Characters.Where(card => card.Info.HasKeywordContaining("巴洛克工作室")).ToList();
        if (active.Count == 0 || costs.Count == 0
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "休息1张咚!!并KO我方1张《巴洛克工作室》角色，使对方所有0费角色本回合无法阻挡？")) return;
        var donAnswer = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RestOwnDon", "选择转为休息状态的咚!!",
            active.Select(don => don.Id.ToString()).ToList(), 1, 1,
            new Dictionary<string, object?>
            {
                ["donChoices"] = active.Select(don => new { id = don.Id.ToString(), state = don.State.ToString() }).ToList(),
            });
        var chosenDon = active.FirstOrDefault(don => donAnswer.Count == 1 && don.Id.ToString() == donAnswer[0]);
        var chosenCost = (await Pick(ctx, ctx.OwnerIndex, "OwnCharacter",
            "选择作为成本KO的《巴洛克工作室》角色", costs, 1, 1)).FirstOrDefault();
        if (chosenDon is null || chosenCost is null || !me.CostArea.Contains(chosenDon)
            || chosenDon.State != DonState.Active || !me.Characters.Contains(chosenCost)
            || !chosenCost.Info.HasKeywordContaining("巴洛克工作室")) return;
        chosenDon.State = DonState.Rest;
        bool paid = await AtomicOps.KOByEffectAsync(ctx.State, ctx.OwnerIndex, chosenCost,
            ctx.Prompts, ctx.OwnerIndex, deferOnKO: true);
        if (!paid)
        {
            if (chosenDon.State == DonState.Rest && chosenDon.AttachedToCardId is null)
                chosenDon.State = DonState.Active;
            return;
        }
        foreach (var card in Opp(ctx).Characters
                     .Where(card => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, card) == 0))
            AtomicOps.AddRestriction(card, RestrictionKind.CannotBeBlocker, KeywordDuration.ThisTurn, ctx.OwnerIndex);
    }

    private static async Task C050(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        await SearchTop(ctx, 4, card => card.HasProperty("知"), 1,
            "确认卡组顶4张，公开最多1张属性（知）卡牌加入手牌", trashRemainder: false);
    }

    private static async Task C051(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        int lifeTotal = Me(ctx).LifeArea.Count + Opp(ctx).LifeArea.Count;
        var target = await ChooseOpponentCharacter(ctx,
            card => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, card) <= lifeTotal,
            $"选择对方最多1张费用不高于双方生命合计（{lifeTotal}）的角色转为休息状态");
        int currentLifeTotal = Me(ctx).LifeArea.Count + Opp(ctx).LifeArea.Count;
        if (target is not null && Opp(ctx).Characters.Contains(target)
            && ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, target) <= currentLifeTotal)
            AtomicOps.RestCard(ctx.State, target);
    }

    private static async Task C052(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            await DrawThenDiscard(ctx, 2, 1);
            return;
        }
        if (ctx.Trigger != EffectTrigger.PreDamageToLeader
            || !IsTriggerAvailable("EB05-052", ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将格罗丽欧莎放置到废弃区，使我方不受到本次伤害？")) return;
        if (!Me(ctx).Characters.Contains(ctx.Source)) return;
        AtomicOps.TrashFieldCard(ctx.State, ctx.OwnerIndex, ctx.Source, ignoreEffectLeaveGuard: true);
        if (Me(ctx).Trash.Contains(ctx.Source))
            EffectRuntime.CommitLeaderDamageReplacement(ctx.Vars);
    }

    private static async Task C053(EffectContext ctx)
    {
        var me = Me(ctx);
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            if (me.LifeArea.Count > 2 || me.Hand.Count == 0
                || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                    "丢弃1张手牌，在生命不多于2张时将此卡登场？")) return;
            if (!await DiscardOwn(ctx, 1, null, "选择作为触发成本丢弃的手牌", isCost: true)
                || !me.Trash.Contains(ctx.Source) || me.LifeArea.Count > 2) return;
            await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source, lifeTriggerOrigin: true);
            return;
        }
        if (ctx.Trigger != EffectTrigger.OnAttackDeclare
            || !IsTriggerAvailable("EB05-053", ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        me.TurnOnceUsed.Add(OnceKey(ctx.Source, "attack"));
        var picked = await Pick(ctx, ctx.OwnerIndex, "OwnHand", "将手牌中最多1张卡牌加入生命区最上方",
            me.Hand, 0, 1);
        if (picked.Count == 1 && me.Hand.Contains(picked[0])) AtomicOps.HandToLife(me, picked[0], toTop: true);
    }

    private static async Task C054(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnLifeRevealTrigger) return;
        var me = Me(ctx);
        var picked = await Pick(ctx, ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多2张力量为4000的《大妈海盗团》角色登场",
            me.Hand.Where(card => card.Info.Kind == CardKind.Character && card.Info.Power == 4000
                && card.Info.HasKeyword("大妈海盗团")), 0, 2);
        foreach (var card in picked)
            if (me.Hand.Contains(card)) await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
    }

    private static async Task C055(EffectContext ctx)
    {
        var me = Me(ctx);
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex || !me.Leader.HasProperty("知")
                || me.Deck.Count == 0
                || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将卡组最上方1张卡牌加入生命区最上方？")) return;
            AtomicOps.AddLifeFromDeckTop(me, 1);
            return;
        }
        if (ctx.Trigger != EffectTrigger.OnLifeRevealTrigger || me.LifeArea.Count > 2 || me.Hand.Count == 0
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "丢弃1张手牌，在生命不多于2张时将此卡登场？")) return;
        if (!await DiscardOwn(ctx, 1, null, "选择作为触发成本丢弃的手牌", isCost: true)
            || !me.Trash.Contains(ctx.Source) || me.LifeArea.Count > 2) return;
        await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source, lifeTriggerOrigin: true);
    }

    private static async Task C056(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField || !Me(ctx).Leader.MatchesName("妮古·罗宾")) return;
        if (Me(ctx).Hand.Any(card => !string.IsNullOrEmpty(card.Info.Trigger))
            && await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "丢弃1张拥有【触发】的手牌以抽取2张卡牌？")
            && await DiscardOwn(ctx, 1, card => !string.IsNullOrEmpty(card.Info.Trigger),
                "可以丢弃1张拥有【触发】的手牌以抽取2张卡牌", isCost: true))
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);
    }

    private static async Task C057(EffectContext ctx)
    {
        var me = Me(ctx);
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            if (me.LifeArea.Count > 2) return;
            var picked = await Pick(ctx, ctx.OwnerIndex, "OwnHandCharacter",
                "将手牌中最多1张力量不高于6000且拥有【触发】的角色登场",
                me.Hand.Where(card => card.Info.Kind == CardKind.Character && card.Info.Power <= 6000
                    && !string.IsNullOrEmpty(card.Info.Trigger)), 0, 1);
            if (picked.Count == 1 && me.Hand.Contains(picked[0]))
                await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked[0]);
            return;
        }
        if (ctx.Trigger != EffectTrigger.ActivatedMain
            || GetActivatedMainUnavailableReason("EB05-057", ctx.State, ctx.OwnerIndex, ctx.Source) is not null) return;
        var targets = new[] { me.Leader }.Concat(me.Characters)
            .Where(card => card.HasProperty("特") || card.HasProperty("知"))
            .ToList();
        var target = (await Pick(ctx, ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择1张属性（特）或（知）的领袖或角色", targets, 1, 1)).FirstOrDefault();
        if (target is null || !me.Characters.Contains(ctx.Source)
            || !(ReferenceEquals(me.Leader, target) || me.Characters.Contains(target))
            || !(target.HasProperty("特") || target.HasProperty("知"))) return;

        int maximum = me.CostArea.Any(don => don.State == DonState.Rest) ? 1 : 0;
        int count = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "选择赋予该卡牌的休息咚!!张数",
            Enumerable.Range(0, maximum + 1).Select(value => $"{value} 张").ToList());
        if (count < 0 || count > maximum || !me.Characters.Contains(ctx.Source)
            || !(ReferenceEquals(me.Leader, target) || me.Characters.Contains(target))
            || !(target.HasProperty("特") || target.HasProperty("知"))) return;
        if (count == 1 && AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest) != 1) return;
        me.TurnOnceUsed.Add(OnceKey(ctx.Source, "main"));
    }

    private static async Task C060(EffectContext ctx)
    {
        var me = Me(ctx);
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            if (me.LifeArea.Count == 0 || me.LifeArea[0].IsLifeFaceUp
                || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                    "将生命区最上方卡牌翻至正面，使我方最多1张领袖或角色本次战斗力量+4000？")) return;
            if (me.LifeArea.Count == 0 || me.LifeArea[0].IsLifeFaceUp) return;
            me.LifeArea[0].IsLifeFaceUp = true;
            var target = (await Pick(ctx, ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "选择我方最多1张领袖或角色，本次战斗力量+4000",
                new[] { me.Leader }.Concat(me.Characters), 0, 1)).FirstOrDefault();
            if (target is not null && ctx.State.SideOf(target) == ctx.OwnerIndex)
                AtomicOps.AddPowerThisBattle(target, 4000);
            return;
        }
        if (ctx.Trigger != EffectTrigger.EventMain) return;
        var costs = me.Characters.Where(card => ctx.State.CurrentCostOf(ctx.OwnerIndex, card) >= 5
            && card.Info.HasKeyword("艾格赫德")).ToList();
        if (costs.Count == 0 || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将1张费用不低于5的《艾格赫德》角色放置到废弃区，将卡组顶加入生命顶？")) return;
        var cost = (await Pick(ctx, ctx.OwnerIndex, "OwnCharacter",
            "选择作为成本放置到废弃区的《艾格赫德》角色", costs, 1, 1)).FirstOrDefault();
        if (cost is null || !me.Characters.Contains(cost) || ctx.State.CurrentCostOf(ctx.OwnerIndex, cost) < 5
            || !cost.Info.HasKeyword("艾格赫德")) return;
        AtomicOps.TrashFieldCard(ctx.State, ctx.OwnerIndex, cost, ignoreEffectLeaveGuard: true);
        if (me.Trash.Contains(cost) && me.Deck.Count > 0
            && await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将卡组最上方1张卡牌加入生命区最上方？"))
            AtomicOps.AddLifeFromDeckTop(me, 1);
    }

    private static async Task C061(EffectContext ctx)
    {
        var me = Me(ctx);
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var picked = await Pick(ctx, ctx.OwnerIndex, "OwnHandCharacter",
                "将手牌中最多1张费用不高于2的红色角色登场",
                me.Hand.Where(card => card.Info.Kind == CardKind.Character && card.Info.Cost <= 2
                    && card.Info.ColorList.Contains("红")), 0, 1);
            if (picked.Count == 1 && me.Hand.Contains(picked[0]))
                await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked[0]);
            return;
        }
        if (ctx.Trigger is not (EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField)
            || !IsTriggerAvailable("EB05-061", ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将生命区最上方1张卡牌加入手牌，使该角色不离场？")) return;
        if (!IsNamiReplacementAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        Guid victimId;
        if (ctx.Trigger == EffectTrigger.PreKO) victimId = ctx.Source.Id;
        else if (!TryReadGuid(ctx.Vars, "victimId", out victimId)) return;
        var life = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        life.IsLifeFaceUp = false;
        me.Hand.Add(life);
        me.TurnOnceUsed.Add(OnceKey(ctx.Source, "leave"));
        ctx.State.MarkPreventEffectLeaveBatch(ctx.OwnerIndex, victimId,
            card => ctx.State.OriginalPowerOf(ctx.OwnerIndex, card) <= 6000,
            isKoReplacement: ctx.Trigger is EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd);
    }
}

/// <summary>EB05 新增卡牌的轻量注册基类。</summary>
public abstract class EB05UpdatedCard : IScriptedEffect, IActivatedMainAvailability,
    ITriggeredEffectAvailability, IFieldStaticEffect
{
    public abstract string CardNumber { get; }
    public bool HandlesTrigger(EffectTrigger trigger) => EB05UpdatedEffects.Handles(CardNumber, trigger);
    public Task Resolve(EffectContext ctx) => EB05UpdatedEffects.Resolve(CardNumber, ctx);
    public string? GetActivatedMainUnavailableReason(GameState state, int ownerIndex, CardInstance source)
        => EB05UpdatedEffects.GetActivatedMainUnavailableReason(CardNumber, state, ownerIndex, source);
    public bool IsTriggerAvailable(GameState state, int ownerIndex, CardInstance source,
        EffectTrigger trigger, IReadOnlyDictionary<string, object?>? payload)
        => EB05UpdatedEffects.IsTriggerAvailable(CardNumber, state, ownerIndex, source, trigger, payload);
    public Task RegisterFieldStatic(EffectContext ctx) => EB05UpdatedEffects.RegisterFieldStatic(CardNumber, ctx);
}

public sealed class EB05_001_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-001"; }
public sealed class EB05_002_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-002"; }
public sealed class EB05_004_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-004"; }
public sealed class EB05_005_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-005"; }
public sealed class EB05_006_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-006"; }
public sealed class EB05_007_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-007"; }
public sealed class EB05_009_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-009"; }
public sealed class EB05_011_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-011"; }
public sealed class EB05_012_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-012"; }
public sealed class EB05_013_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-013"; }
public sealed class EB05_014_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-014"; }
public sealed class EB05_017_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-017"; }
public sealed class EB05_018_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-018"; }
public sealed class EB05_020_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-020"; }
public sealed class EB05_021_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-021"; }
public sealed class EB05_022_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-022"; }
public sealed class EB05_023_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-023"; }
public sealed class EB05_024_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-024"; }
public sealed class EB05_025_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-025"; }
public sealed class EB05_027_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-027"; }
public sealed class EB05_028_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-028"; }
public sealed class EB05_029_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-029"; }
public sealed class EB05_031_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-031"; }
public sealed class EB05_034_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-034"; }
public sealed class EB05_035_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-035"; }
public sealed class EB05_036_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-036"; }
public sealed class EB05_037_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-037"; }
public sealed class EB05_038_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-038"; }
public sealed class EB05_039_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-039"; }
public sealed class EB05_042_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-042"; }
public sealed class EB05_043_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-043"; }
public sealed class EB05_044_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-044"; }
public sealed class EB05_045_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-045"; }
public sealed class EB05_046_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-046"; }
public sealed class EB05_047_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-047"; }
public sealed class EB05_048_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-048"; }
public sealed class EB05_050_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-050"; }
public sealed class EB05_051_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-051"; }
public sealed class EB05_052_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-052"; }
public sealed class EB05_053_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-053"; }
public sealed class EB05_054_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-054"; }
public sealed class EB05_055_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-055"; }
public sealed class EB05_056_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-056"; }
public sealed class EB05_057_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-057"; }
public sealed class EB05_060_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-060"; }
public sealed class EB05_061_Updated : EB05UpdatedCard { public override string CardNumber => "EB05-061"; }
