using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>十月二十九日卡牌共用的严格选择、成本与目标复验。</summary>
internal static class October29Effects
{
    internal static PlayerState Me(EffectContext c) => c.State.Players[c.OwnerIndex];
    internal static string Once(CardInstance source, string effect = "main") => $"{source.Info.Number}:{effect}:{source.Id}";
    internal static bool Field(GameState s, int owner, CardInstance card) => s.Players[owner].Characters.Contains(card);
    internal static Task<List<CardInstance>> Pick(EffectContext c, IEnumerable<CardInstance> cards, string text,
        string kind = "OwnCharacter", int min = 0, int max = 1)
        => October9CardHelpers.Pick(c, c.OwnerIndex, kind, text, cards, min, max);

    // 复合成本先收集全部选择，再无等待提交，取消或过期响应不会部分支付。
    internal static async Task<bool> DiscardCost(EffectContext c, int count, Func<CardInstance, bool>? filter = null, int restDon = 0, Func<bool>? stillValid = null)
    {
        var me = Me(c); filter ??= _ => true;
        var dons = me.CostArea.Where(d => d.State == DonState.Active).Take(restDon).ToList();
        if (dons.Count != restDon || me.Hand.Count(filter) < count) return false;
        var cards = await Pick(c, me.Hand.Where(filter), $"可以丢弃{count}张符合条件的手牌作为成本，或取消发动", "OwnHandDiscard", 0, count);
        if (cards.Count != count || cards.Any(x => !me.Hand.Contains(x) || !filter(x)) || !(stillValid?.Invoke() ?? true)
            || dons.Any(d => !me.CostArea.Contains(d) || d.State != DonState.Active)) return false;
        bool previous = EffectRuntime.PayingCost; EffectRuntime.PayingCost = true;
        try
        {
            foreach (var don in dons) don.State = DonState.Rest;
            foreach (var card in cards) AtomicOps.DiscardHand(me, card);
        }
        finally { EffectRuntime.PayingCost = previous; }
        return true;
    }

    internal static async Task Buff(EffectContext c, int delta, bool battle = false, bool leader = true,
        Func<CardInstance, bool>? filter = null, string? keyword = null, bool opponent = false)
    {
        int side = opponent ? 1 - c.OwnerIndex : c.OwnerIndex;
        var player = c.State.Players[side];
        var candidates = leader ? player.Characters.Prepend(player.Leader) : player.Characters;
        var picked = await Pick(c, candidates.Where(x => filter?.Invoke(x) ?? true),
            keyword is null ? $"选择最多1张卡牌，{(battle ? "本次战斗" : "本回合")}力量{delta:+0;-0;0}"
                : $"选择最多1张符合条件的卡牌，本回合获得{keyword}", opponent ? "OppCharacter" : "OwnLeaderOrCharacter");
        if (picked.Count != 1 || !(player.Characters.Contains(picked[0]) || (leader && player.Leader == picked[0]))
            || !(filter?.Invoke(picked[0]) ?? true)) return;
        if (keyword is not null) AtomicOps.GiveKeyword(picked[0], keyword, KeywordDuration.ThisTurn);
        else if (battle) AtomicOps.AddPowerThisBattle(picked[0], delta);
        else AtomicOps.AddPowerThisTurn(picked[0], delta);
    }

    internal static async Task Move(EffectContext c, int limit, string destination, bool anySide = false,
        bool originalCost = false, bool rested = false)
    {
        bool Eligible(int side, CardInstance x) => (originalCost ? x.Info.Cost : c.State.CurrentCostOf(side, x)) <= limit
            && (!rested || x.IsTapped)
            && (destination != "rest" || (!x.IsTapped && AtomicOps.CanRestCard(c.State, x, side)));
        var candidates = c.State.Players.SelectMany((p, side) => p.Characters
            .Where(x => (anySide || side != c.OwnerIndex) && Eligible(side, x)).Select(x => (side, card: x))).ToList();
        var picked = await Pick(c, candidates.Select(x => x.card), $"选择最多1张费用不高于{limit}的角色",
            anySide ? "AnyCharacter" : "OppCharacter");
        if (picked.Count != 1) return;
        var target = candidates.First(x => x.card == picked[0]);
        if (!Field(c.State, target.side, target.card) || !Eligible(target.side, target.card)) return;
        if (destination == "ko")
            await AtomicOps.KOByEffectAsync(c.State, target.side, target.card, c.Prompts, c.OwnerIndex);
        else if (destination == "rest") AtomicOps.RestCard(c.State, target.card, target.side);
        else await AtomicOps.ProcessEffectLeavesAsync(c.State, target.side, [target.card], c.Prompts, destination,
            destination == "hand" ? (s, side, card) => AtomicOps.BounceToHand(s, side, card) : AtomicOps.ReturnFieldToDeckBottom);
    }

    internal static async Task Restrict(EffectContext c, int limit, RestrictionKind restriction)
    {
        int side = 1 - c.OwnerIndex; var cards = c.State.Players[side].Characters;
        var picked = await Pick(c, cards.Where(x => c.State.CurrentCostOf(side, x) <= limit),
            $"选择对方最多1张费用不高于{limit}的角色，直到下个对方结束阶段结束前施加限制", "OppCharacter");
        if (picked.Count == 1 && cards.Contains(picked[0]) && c.State.CurrentCostOf(side, picked[0]) <= limit)
            AtomicOps.AddRestriction(picked[0], restriction, KeywordDuration.UntilNextOpponentEndPhase, c.OwnerIndex);
    }

    internal static async Task AttachRestDon(EffectContext c, bool dressrosa = false)
    {
        var me = Me(c);
        var picked = await Pick(c, me.Characters.Where(x => !dressrosa || x.Info.HasKeyword("德莱斯罗兹")).Prepend(me.Leader),
            "选择领袖或符合条件的角色，赋予最多1张休息咚", "OwnLeaderOrCharacter");
        if (picked.Count != 1 || !(me.Leader == picked[0] || me.Characters.Contains(picked[0]))) return;
        if (dressrosa && me.Leader != picked[0] && !picked[0].Info.HasKeyword("德莱斯罗兹")) return;
        var target = picked[0];
        await AtomicOps.PromptChooseAndApplyDonCount(c.State, c.Prompts, c.OwnerIndex, 1,
            "选择赋予的休息咚数量（0至1张）", d => d.State == DonState.Rest,
            d => { if (me.Leader != target && !me.Characters.Contains(target)) return;
                d.State = DonState.Attached; d.AttachedToCardId = target.Id; });
    }

    internal static async Task SearchTop(EffectContext c, int count, Func<CardInstance, bool> filter)
    {
        var me = Me(c); var top = me.Deck.Take(count).ToList();
        if (top.Count == 0) return;
        var choices = top.Where(filter).ToList();
        var answer = await c.Prompts.ChooseCards(c.OwnerIndex, "LookTop", "确认卡组顶牌，公开最多1张符合条件的卡牌加入手牌",
            choices.Select(x => x.Id.ToString()).ToList(), 0, Math.Min(1, choices.Count),
            new Dictionary<string, object?> { ["choiceCards"] = top.Select(x => new { id = x.Id.ToString(), number = x.Info.Number }).ToList() });
        if (answer.Count > 1 || answer.Any(id => choices.All(x => x.Id.ToString() != id))) return;
        var selected = choices.FirstOrDefault(x => x.Id.ToString() == answer.FirstOrDefault());
        var rest = top.Where(x => x != selected).ToList();
        var order = rest.Count > 1 ? await Pick(c, rest, "将剩余卡牌自选顺序放回卡组底（先选者在上方）", "ReorderToDeckBottom", rest.Count, rest.Count) : rest;
        if (order.Count != rest.Count || !me.Deck.Take(top.Count).SequenceEqual(top) || (selected is not null && !filter(selected))) return;
        me.Deck.RemoveRange(0, top.Count);
        if (selected is not null) { me.Hand.Add(selected); c.BroadcastReveal(selected); }
        me.Deck.AddRange(order);
    }

    internal static async Task ReviveSelf(EffectContext c, int discard, bool rest = false, string? leaderFeature = null)
    {
        var me = Me(c);
        if (!me.Trash.Contains(c.Source) || me.Hand.Count < discard || !await DiscardCost(c, discard)) return;
        if (me.Trash.Contains(c.Source) && (leaderFeature is null || me.Leader.Info.HasKeyword(leaderFeature)))
            await AtomicOps.PlayFromTrashFree(c.State, c.OwnerIndex, c.Source, restState: rest);
    }

    internal static async Task Reorder(EffectContext c, bool life)
    {
        var me = Me(c); var cards = life ? me.LifeArea.ToList() : me.Deck.Take(3).ToList();
        if (cards.Count == 0) return;
        var order = await Pick(c, cards, life ? "确认全部生命，自选顶到底的顺序" : "确认卡组顶3张，自选顶到底的顺序",
            life ? "OwnLifeReorder" : "DeckReorder", cards.Count, cards.Count);
        if (order.Count != cards.Count) return;
        int where = life ? 0 : await c.Prompts.ChooseOption(c.OwnerIndex, "放回卡组的位置", ["最上方", "最下方"]);
        if (where is not (0 or 1)) return;
        if (life)
        {
            if (me.LifeArea.SequenceEqual(cards)) AtomicOps.ReorderLife(me, order.Select(x => x.Id).ToList());
        }
        else if (me.Deck.Take(cards.Count).SequenceEqual(cards)) AtomicOps.ReorderTopK(me, order.Select(x => x.Id).ToList(), where == 1);
    }

    internal static bool Replacement(GameState s, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
    {
        if (!Field(s, owner, source)) return false;
        var victim = October9CardHelpers.Victim(s, owner, source, trigger, payload);
        if (victim is null) return false;
        if (source.Info.Number == "OP18-054" && s.Players[owner].Hand.Count == 0) return false;
        if (source.Info.Number is "OP18-068" or "OP18-071" && !s.Players[owner].CostArea.Any(d => d.State is DonState.Active or DonState.Rest or DonState.Attached)) return false;
        if (source.Info.Number == "OP18-068") return victim == source && !s.Players[owner].TurnOnceUsed.Contains(Once(source, "guard"));
        string feature = source.Info.Number switch { "OP18-026" => "草帽一伙", "OP18-054" => "巴洛克工作室", _ => "堂吉诃德海盗团" };
        bool match = source.Info.Number == "OP18-054" ? victim.Info.HasKeywordContaining(feature) : victim.Info.HasKeyword(feature);
        return match && October9CardHelpers.OpponentEffect(s, owner, trigger, payload)
            && (source.Info.Number != "OP18-054" || s.CurrentTurnPlayer != owner)
            && (source.Info.Number == "OP18-026" || !s.Players[owner].TurnOnceUsed.Contains(Once(source, "guard")));
    }

    internal static async Task Guard(EffectContext c)
    {
        if (!Replacement(c.State, c.OwnerIndex, c.Source, c.Trigger, c.Vars)) return;
        var victim = October9CardHelpers.Victim(c.State, c.OwnerIndex, c.Source, c.Trigger, c.Vars)!;
        string number = c.Source.Info.Number;
        bool paid = number switch
        {
            "OP18-026" => await TrashGuardSelf(c),
            "OP18-054" => await DiscardCost(c, 1, stillValid: () => Replacement(c.State, c.OwnerIndex, c.Source, c.Trigger, c.Vars)),
            _ => await GuardDon(c),
        };
        if (!paid || !Field(c.State, c.OwnerIndex, victim)) return;
        if (number != "OP18-026") Me(c).TurnOnceUsed.Add(Once(c.Source, "guard"));
        if (number == "OP18-068") { c.State.MarkPreventKO(victim.Id); return; }
        string feature = number switch { "OP18-026" => "草帽一伙", "OP18-054" => "巴洛克工作室", _ => "堂吉诃德海盗团" };
        c.State.MarkPreventEffectLeaveBatch(c.OwnerIndex, victim.Id,
            x => number == "OP18-054" ? x.Info.HasKeywordContaining(feature) : x.Info.HasKeyword(feature),
            isKoReplacement: c.Trigger is EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd);
    }

    private static async Task<bool> TrashGuardSelf(EffectContext c)
    {
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将山智放置到废弃区，使草帽角色不离场？")
            || !Replacement(c.State, c.OwnerIndex, c.Source, c.Trigger, c.Vars)) return false;
        bool previous = EffectRuntime.PayingCost; EffectRuntime.PayingCost = true;
        try { AtomicOps.TrashFieldCard(c.State, c.OwnerIndex, c.Source, ignoreEffectLeaveGuard: true); }
        finally { EffectRuntime.PayingCost = previous; }
        return Me(c).Trash.Contains(c.Source);
    }

    private static async Task<bool> GuardDon(EffectContext c)
    {
        var me = Me(c); var dons = me.CostArea.Where(d => d.State is DonState.Active or DonState.Rest or DonState.Attached).ToList();
        if (dons.Count == 0) return false;
        var answer = await c.Prompts.ChooseCards(c.OwnerIndex, "ReturnOwnDon", "可以放回1张咚作为替代离场成本，或取消发动",
            dons.Select(d => d.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?> { ["donChoices"] = AtomicOps.BuildDonPromptChoices(me, dons), ["canCancel"] = true });
        if (answer.Count != 1 || !Replacement(c.State, c.OwnerIndex, c.Source, c.Trigger, c.Vars)) return false;
        var chosen = dons.FirstOrDefault(d => d.Id.ToString() == answer[0]);
        if (chosen is null || !me.CostArea.Contains(chosen) || chosen.State is not (DonState.Active or DonState.Rest or DonState.Attached)) return false;
        me.CostArea.Remove(chosen); chosen.State = DonState.InDeck; chosen.AttachedToCardId = null; me.DonDeck.Add(chosen);
        EffectRuntime.NotifyWatcher(EffectTrigger.OnDonReturnedToDeck, new Dictionary<string, object?> { ["owner"] = c.OwnerIndex, ["count"] = 1 });
        return true;
    }

    internal static Task RegisterStatic(EffectContext c)
    {
        int owner = c.OwnerIndex; var self = c.Source; string number = self.Info.Number;
        var s = c.State; string id = self.Id.ToString();
        s.ContinuousEffects.RemoveAll(x => x.SourceCardId == id);
        void Self(int power = 0, int cost = 0, string? keyword = null,
            Func<GameState, bool>? condition = null, Func<GameState, int, CardInstance, int>? resolver = null)
            => s.ContinuousEffects.Add(new ContinuousEffect { SourceCardId = id,
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false }, PowerDelta = power, CostDelta = cost,
                GrantKeyword = keyword, PowerDeltaResolver = resolver,
                Predicate = (state, side, card) => side == owner && card == self && (condition?.Invoke(state) ?? true) });
        bool Leader(GameState state, string feature) => state.Players[owner].Leader.Info.HasKeyword(feature);
        switch (number)
        {
            case "OP18-004":
                foreach (var (threshold, keyword) in new[] { (5000, "速攻"), (6000, "流放"), (7000, "不可阻挡") })
                    Self(keyword: keyword, condition: state => state.CurrentPowerOf(owner, self) >= threshold);
                break;
            case "OP18-005": Self(condition: state => state.CurrentTurnPlayer == owner,
                resolver: (state, _, _) => state.Players.Sum(p => p.Characters.Count(x => x.MatchesName("功夫海牛"))) * 1000); break;
            case "OP18-006":
                bool Vivi(GameState state) => state.Players[owner].Characters.Prepend(state.Players[owner].Leader).Any(x => x.MatchesName("奈菲特·薇薇"));
                Self(1000, keyword: "速攻", condition: Vivi); break;
            case "OP18-007": Self(1000, keyword: "流放", condition: state => Leader(state, "海军")); break;
            case "OP18-008": Self(keyword: "速攻", condition: state => Leader(state, "阿拉巴斯坦王国")); break;
            case "OP18-027":
                s.ContinuousEffects.Add(new ContinuousEffect { SourceCardId = id, Scope = new ContinuousScope { Side = 0, IncludeCharacters = false },
                    OriginalPowerOverride = 7000, Predicate = (state, side, card) => side == owner && card == state.Players[owner].Leader
                        && state.CurrentTurnPlayer != owner && card.MatchesName("蒙奇·D·路飞") && !card.IsTapped
                        && state.Players[owner].AttachedDonCount(card.Id) >= 3 }); break;
            case "OP18-030": Self(-2000, condition: state => state.CurrentTurnPlayer != owner && self.IsTapped); break;
            case "OP18-045": Self(resolver: (state, _, _) => state.Players[owner].Trash.Count(x => x.Info.Kind == CardKind.Event) / 5 * 1000); break;
            case "OP18-053": case "OP18-054": Self(1000, condition: state => state.Players[owner].AttachedDonCount(self.Id) >= 1); break;
            case "OP18-071": Self(2000, keyword: "阻挡者", condition: state => state.CurrentTurnPlayer != owner && state.Players[owner].Leader.MatchesName("糖糖")); break;
            case "OP18-081":
                foreach (string keyword in new[] { "阻挡者", "双重攻击" }) Self(keyword: keyword,
                    condition: state => state.Players[owner].Characters.Any(x => x.MatchesName("月光·莫利亚"))); break;
            case "OP18-087":
                Self(cost: 12); Self(2000, condition: state => state.Players[owner].Characters.All(x => state.CurrentCostOf(owner, x) >= 12)); break;
            case "OP18-097":
                s.ContinuousEffects.Add(new ContinuousEffect { SourceCardId = id,
                    Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = false, IncludeHand = true }, CostDelta = -1,
                    Predicate = (state, side, card) => side == owner && Leader(state, "天龙人") && card.Info.Kind == CardKind.Character
                        && card.Info.Cost >= 2 && (card.Info.HasKeyword("神之骑士团") || card.Info.HasKeyword("五老星")) }); break;
        }
        return Task.CompletedTask;
    }
}

/// <summary>每张卡独立登记；持续效果在登场无效判定之前注册，交互均复验实际区域。</summary>
public abstract class October29Card : IScriptedEffect, IFieldStaticEffect, IActivatedMainAvailability, ITriggeredEffectAvailability, IEventMainAvailability
{
    public abstract string CardNumber { get; }
    public Task RegisterFieldStatic(EffectContext c) => October29Effects.RegisterStatic(c);
    public bool HandlesTrigger(EffectTrigger trigger) => CardNumber switch
    {
        "OP18-002" or "OP18-008" or "OP18-023" or "OP18-027" or "OP18-049" or "OP18-050" or "OP18-053" or "OP18-067" or "OP18-097" => trigger == EffectTrigger.OnEnterField,
        "OP18-009" or "OP18-072" => trigger == EffectTrigger.ActivatedMain,
        "OP18-012" or "OP18-014" => trigger == EffectTrigger.OnAttackDeclare,
        "OP18-019" or "OP18-057" or "OP18-059" or "OP18-096" => trigger is EffectTrigger.EventMain or EffectTrigger.EventCounter,
        "OP18-020" or "OP18-039" => trigger == EffectTrigger.EventCounter,
        "OP18-026" or "OP18-054" or "OP18-071" => trigger is EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField,
        "OP18-033" => trigger == EffectTrigger.OnMyTurnEnd,
        "OP18-037" => trigger == EffectTrigger.OnKO,
        "OP18-038" or "OP18-040" or "OP18-058" or "OP18-094" or "OP18-095" or "OP18-115" => trigger is EffectTrigger.EventMain or EffectTrigger.OnLifeRevealTrigger,
        "OP18-042" => trigger is EffectTrigger.ActivatedMain or EffectTrigger.OnKO,
        "OP18-068" => trigger is EffectTrigger.OnEnterField or EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd,
        "OP18-074" or "OP18-116" => trigger is EffectTrigger.EventCounter or EffectTrigger.OnLifeRevealTrigger,
        "OP18-080" => trigger is EffectTrigger.OnHandDiscarded or EffectTrigger.OnKO,
        "OP18-081" or "OP18-105" or "OP18-107" or "OP18-109" => trigger == EffectTrigger.OnLifeRevealTrigger,
        "OP18-082" => trigger is EffectTrigger.OnAttackDeclare or EffectTrigger.OnKO,
        "OP18-083" or "OP18-088" => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnKO,
        "OP18-099" or "OP18-110" or "OP18-114" => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnLifeRevealTrigger,
        "OP18-117" => trigger == EffectTrigger.EventMain,
        _ => false,
    };

    public string? GetActivatedMainUnavailableReason(GameState state, int owner, CardInstance source)
    {
        if (state.CurrentTurnPlayer != owner) return "只能在我方主要阶段发动";
        var me = state.Players[owner];
        if (!me.Characters.Contains(source)) return "来源角色已不在场上";
        if (CardNumber is "OP18-042" or "OP18-072" && me.TurnOnceUsed.Contains(October29Effects.Once(source))) return "本回合已经发动";
        if (CardNumber == "OP18-072" && !me.StageCards.Any(x => state.CurrentCostOf(owner, x) <= 5)) return "需要费用不高于5的舞台";
        return null;
    }

    public string? GetEventMainUnavailableReason(GameState state, int owner, CardInstance source, int effectivePlayCost)
    {
        var me = state.Players[owner];
        int extra = CardNumber is "OP18-057" or "OP18-096" ? 3 : CardNumber == "OP18-059" ? 1 : 0;
        if (me.CostArea.Count(x => x.State == DonState.Active) < effectivePlayCost + extra) return "没有足够活跃咚支付事件与效果成本";
        if (CardNumber == "OP18-058" && me.Characters.Count == 0) return "需要1张我方角色作为返回手牌成本";
        if (CardNumber == "OP18-059" && !me.Hand.Any(x => x != source && x.Info.HasKeyword("巴洛克工作室"))) return "需要1张巴洛克工作室手牌作为成本";
        return null;
    }

    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger, IReadOnlyDictionary<string, object?>? payload)
    {
        var me = state.Players[owner];
        if (trigger is EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField)
            return October29Effects.Replacement(state, owner, source, trigger, payload);
        if (trigger == EffectTrigger.ActivatedMain) return GetActivatedMainUnavailableReason(state, owner, source) is null;
        if (trigger == EffectTrigger.OnMyTurnEnd) return state.CurrentTurnPlayer == owner && me.Characters.Contains(source);
        if (trigger == EffectTrigger.OnHandDiscarded)
            return state.CurrentTurnPlayer == owner && me.Characters.Contains(source)
                && !me.TurnOnceUsed.Contains(October29Effects.Once(source, "discard"))
                && !me.Characters.Any(x => x != source && x.MatchesName("旺·奥加"))
                && payload?.TryGetValue("owner", out var side) == true && side is int discardOwner && discardOwner == owner
                && payload.TryGetValue("sourceNumber", out var number) && number is string text && text == me.Leader.Info.Number
                && me.Leader.MatchesName("马歇尔·D·提奇");
        if (trigger == EffectTrigger.OnEnterField && CardNumber is "OP18-083" or "OP18-088") return state.CurrentTurnPlayer == owner;
        if (trigger == EffectTrigger.OnLifeRevealTrigger && CardNumber is "OP18-099" or "OP18-105" or "OP18-107" or "OP18-109" or "OP18-114" or "OP18-115" or "OP18-116")
            return me.Leader.Info.HasKeywordContaining("CP");
        return true;
    }

    public async Task Resolve(EffectContext c)
    {
        if (!HandlesTrigger(c.Trigger) || !IsTriggerAvailable(c.State, c.OwnerIndex, c.Source, c.Trigger, c.Vars)) return;
        var me = October29Effects.Me(c); var s = c.State; int owner = c.OwnerIndex;
        if (c.Trigger is EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField)
        { await October29Effects.Guard(c); return; }
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger) { await Life(c); return; }
        switch (CardNumber)
        {
            case "OP18-002": case "OP18-067":
                if (await October29Effects.DiscardCost(c, 1, x => x.Info.HasKeyword(CardNumber == "OP18-002" ? "阿拉巴斯坦王国" : "大妈海盗团")))
                    await AtomicOps.DrawAsync(s, owner, 2);
                break;
            case "OP18-008":
                if (await October29Effects.DiscardCost(c, 1, x => x.Info.Kind == CardKind.Character && x.Info.Power <= 5000))
                { await AtomicOps.DrawAsync(s, owner, 2); if (!s.IsGameOver && me.Characters.Contains(c.Source)) AtomicOps.GiveKeyword(c.Source, "流放", KeywordDuration.ThisTurn); }
                break;
            case "OP18-009":
                if (!await October20CardHelpers.TrashSelf(c, "将此角色放置到废弃区，尝试登场手牌中的卡鲁？") || me.TotalDonInCostArea < 9) break;
                var karoo = await October29Effects.Pick(c, me.Hand.Where(x => x.Info.Kind == CardKind.Character && x.MatchesName("卡鲁")), "登场手牌中最多1张卡鲁", "OwnHandCharacter");
                if (karoo.Count == 1 && me.Hand.Contains(karoo[0]) && karoo[0].MatchesName("卡鲁")) await AtomicOps.PlayFromHandFree(s, owner, karoo[0]); break;
            case "OP18-012": await October29Effects.Buff(c, 1000, leader: false, filter: x => x.Info.HasKeyword("动物") && x.Info.HasKeyword("阿拉巴斯坦王国")); break;
            case "OP18-014": await October29Effects.Buff(c, 0, leader: false, filter: x => x.Info.HasKeyword("阿拉巴斯坦王国"), keyword: "速攻"); break;
            case "OP18-019":
                if (c.Trigger == EffectTrigger.EventCounter) AtomicOps.AddPowerThisBattle(me.Leader, 3000);
                else if (!me.Characters.Any(x => s.OriginalPowerOf(owner, x) >= 7000)) await October29Effects.Buff(c, -3000, leader: false, opponent: true); break;
            case "OP18-020":
                if (me.LifeArea.Count > 0 && s.NoEffectLifeToHandThisTurn.Contains(owner)) break;
                if (!await c.Prompts.ConfirmOptional(owner, "将我方所有生命加入手牌作为成本？")) break;
                if (me.LifeArea.Count > 0 && s.NoEffectLifeToHandThisTurn.Contains(owner)) break;
                while (me.LifeArea.Count > 0) OfficialCoverageHelpers.LifeTopToHand(s, owner);
                if (me.Leader.MatchesName("奈菲特·薇薇")) AtomicOps.AddPowerToAllThisTurn(s, 1 - owner, _ => true, -5000); break;
            case "OP18-023": await October29Effects.Buff(c, 0, filter: x => x.Info.HasKeyword("东海"), keyword: "流放"); break;
            case "OP18-027":
                var dons = me.CostArea.Where(x => x.State == DonState.Rest).ToList();
                if (dons.Count > 0)
                {
                    var ids = await c.Prompts.ChooseCards(owner, "OwnRestDon", "将最多2张休息咚转为活跃", dons.Select(x => x.Id.ToString()).ToList(), 0, Math.Min(2, dons.Count),
                        new Dictionary<string, object?> { ["donChoices"] = AtomicOps.BuildDonPromptChoices(me, dons) });
                    if (ids.Count <= 2 && ids.Distinct().Count() == ids.Count && ids.All(id => dons.Any(x => x.Id.ToString() == id)))
                        foreach (var don in dons.Where(x => ids.Contains(x.Id.ToString()) && me.CostArea.Contains(x) && x.State == DonState.Rest)) don.State = DonState.Active;
                }
                await October29Effects.Restrict(c, 6, RestrictionKind.CannotBeRested); break;
            case "OP18-033":
                if (!await October20CardHelpers.TrashSelf(c, "将烹烹放置到废弃区，重置最多1张我方角色？")) break;
                var activate = await October29Effects.Pick(c, me.Characters, "将最多1张我方角色转为活跃状态");
                if (activate.Count == 1 && me.Characters.Contains(activate[0])) AtomicOps.ActivateCard(activate[0]); break;
            case "OP18-037":
                var opp = s.Players[1 - owner];
                var rest = await October29Effects.Pick(c, opp.Characters.Prepend(opp.Leader).Concat(opp.StageCards), "将对方最多1张卡牌转为休息状态", "OppFieldCard");
                if (rest.Count == 1 && (opp.Characters.Contains(rest[0]) || opp.Leader == rest[0] || opp.StageCards.Contains(rest[0]))) AtomicOps.RestCard(s, rest[0], 1 - owner); break;
            case "OP18-038": await C038(c); break;
            case "OP18-039":
                await October29Effects.Buff(c, 2000, battle: true, filter: x => x.MatchesName("蒙奇·D·路飞"));
                if (!me.Leader.IsTapped) await AtomicOps.DrawAsync(s, owner, 1); break;
            case "OP18-040": await October29Effects.Move(c, 7, "ko", rested: true); break;
            case "OP18-042":
                if (c.Trigger == EffectTrigger.OnKO) await AtomicOps.DrawAsync(s, owner, 1);
                else { me.TurnOnceUsed.Add(October29Effects.Once(c.Source)); await October29Effects.AttachRestDon(c, dressrosa: true); } break;
            case "OP18-049":
                await C049(c); break;
            case "OP18-050": if (me.Leader.Info.HasKeywordContaining("巴洛克工作室")) await October29Effects.Restrict(c, 7, RestrictionKind.CannotAttack); break;
            case "OP18-053": await October29Effects.Reorder(c, life: false); break;
            case "OP18-057":
                if (c.Trigger == EffectTrigger.EventCounter) AtomicOps.AddPowerThisBattle(me.Leader, 3000);
                else if (await EB05UpdatedEffects.RestDonCost(c, 3, "可以休息3张活跃咚作为成本，或取消发动")) await October29Effects.Move(c, 5, "hand", anySide: true); break;
            case "OP18-058": await ReturnCharacterCost(c); break;
            case "OP18-059":
                if (c.Trigger == EffectTrigger.EventMain)
                { if (await October29Effects.DiscardCost(c, 1, x => x.Info.HasKeyword("巴洛克工作室"), restDon: 1)) await AtomicOps.DrawAsync(s, owner, 2); }
                else if (await October29Effects.DiscardCost(c, 1)) await October29Effects.Buff(c, 3000, battle: true); break;
            case "OP18-068": if (await AtomicOps.PromptReturnDonToDeck(c, 1)) await October29Effects.Restrict(c, 7, RestrictionKind.CannotBeRested); break;
            case "OP18-072":
                var stage = await October29Effects.Pick(c, me.StageCards.Where(x => s.CurrentCostOf(owner, x) <= 5), "可以废弃1张费用不高于5的舞台作为成本", "OwnStage");
                if (stage.Count != 1 || !me.StageCards.Contains(stage[0]) || s.CurrentCostOf(owner, stage[0]) > 5
                    || GetActivatedMainUnavailableReason(s, owner, c.Source) is not null) break;
                bool previous = EffectRuntime.PayingCost; EffectRuntime.PayingCost = true;
                try { AtomicOps.TrashFieldCard(s, owner, stage[0], ignoreEffectLeaveGuard: true); } finally { EffectRuntime.PayingCost = previous; }
                me.TurnOnceUsed.Add(October29Effects.Once(c.Source));
                if (c.Source.TurnPlayed == s.TurnCount) await October29Effects.Buff(c, -3000, leader: false, opponent: true); break;
            case "OP18-074": if (me.StageCards.Any(x => s.CurrentCostOf(owner, x) >= 5)) await October29Effects.Buff(c, 4000, battle: true); break;
            case "OP18-080":
                if (c.Trigger == EffectTrigger.OnHandDiscarded) me.TurnOnceUsed.Add(October29Effects.Once(c.Source, "discard"));
                await AtomicOps.DrawAsync(s, owner, 1); break;
            case "OP18-082":
                if (c.Trigger == EffectTrigger.OnAttackDeclare) c.Source.OriginalPowerOverride = s.CurrentPowerOf(1 - owner, s.Players[1 - owner].Leader);
                else await October29Effects.ReviveSelf(c, 1, rest: true, leaderFeature: "黑胡子海盗团"); break;
            case "OP18-083":
                if (c.Trigger == EffectTrigger.OnKO) await October29Effects.ReviveSelf(c, 2);
                else
                {
                    var mma = await October29Effects.Pick(c, me.Trash.Where(x => x.Info.Kind == CardKind.Character && x.MatchesName("MMA")), "登场废弃区中最多1张MMA", "OwnTrashCharacter");
                    if (mma.Count == 1 && me.Trash.Contains(mma[0]) && mma[0].MatchesName("MMA")) await AtomicOps.PlayFromTrashFree(s, owner, mma[0]);
                } break;
            case "OP18-088":
                if (c.Trigger == EffectTrigger.OnKO) await October29Effects.ReviveSelf(c, 2);
                else if (me.TotalDonInCostArea >= 6) await October29Effects.Move(c, 4, "ko", originalCost: true); break;
            case "OP18-094": await C094(c); break;
            case "OP18-095": await C095(c); break;
            case "OP18-096":
                if (c.Trigger == EffectTrigger.EventCounter)
                { if (me.Trash.Count >= 10) await October29Effects.Buff(c, 4000, battle: true); }
                else if (await EB05UpdatedEffects.RestDonCost(c, 3, "可以休息3张活跃咚作为成本，或取消发动"))
                {
                    bool Eligible(CardInstance x) => x.Info.Kind == CardKind.Character && x.Info.HasKeyword("神之骑士团") && x.Info.Cost <= 6;
                    var revive = await October29Effects.Pick(c, me.Trash.Where(Eligible), "登场废弃区中最多1张费用不高于6的神之骑士团角色", "OwnTrashCharacter");
                    if (revive.Count == 1 && me.Trash.Contains(revive[0]) && Eligible(revive[0])) await AtomicOps.PlayFromTrashFree(s, owner, revive[0]);
                } break;
            case "OP18-097":
                await AtomicOps.DrawAsync(s, owner, 1); if (s.IsGameOver) break;
                var mill = me.Deck.Take(2).ToList(); me.Deck.RemoveRange(0, mill.Count); me.Trash.AddRange(mill); break;
            case "OP18-099":
                var lifeCard = await October29Effects.Pick(c, me.Hand.Where(x => x.Info.HasKeywordContaining("CP")), "将手牌中最多1张CP卡正面朝上加入生命区", "OwnHand");
                if (lifeCard.Count != 1) break;
                int where = await c.Prompts.ChooseOption(owner, "加入生命区的位置", ["最上方", "最下方"]);
                if (where is 0 or 1 && me.Hand.Contains(lifeCard[0]) && lifeCard[0].Info.HasKeywordContaining("CP")) AtomicOps.HandToLife(me, lifeCard[0], toTop: where == 0, faceUp: true); break;
            case "OP18-110": await C110(c); break;
            case "OP18-114": await October29Effects.Reorder(c, life: true); break;
            case "OP18-115": await October29Effects.Move(c, 5, "ko"); break;
            case "OP18-116": if (await October29Effects.DiscardCost(c, 1)) await October29Effects.Buff(c, 3000, battle: true); break;
            case "OP18-117": await BusterCall(c); break;
        }
    }

    private static Task C038(EffectContext c)
        => October29Effects.SearchTop(c, 5, x => x.MatchesName("狙击王") || x.Info.HasKeyword("草帽一伙"));
    private static async Task C049(EffectContext c)
    {
        await October29Effects.SearchTop(c, 5, x => x.Info.HasKeywordContaining("巴洛克工作室"));
        if (!c.State.IsGameOver) await October29Effects.AttachRestDon(c);
    }
    private static Task C094(EffectContext c)
        => October29Effects.SearchTop(c, 5, x => x.Info.HasKeyword("埃鲁巴夫"));
    private static Task C095(EffectContext c)
        => October29Effects.Me(c).Leader.Info.HasKeyword("天龙人")
            ? October29Effects.SearchTop(c, 5, x => x.MatchesName("MMA") || x.Info.HasKeyword("神之骑士团")) : Task.CompletedTask;
    private static async Task C105(EffectContext c)
    {
        var me = October29Effects.Me(c);
        if (!me.Leader.Info.HasKeywordContaining("CP")) return;
        var reveal = await October29Effects.Pick(c, me.Hand.Where(x => !x.MatchesName("斯巴达姆") && x.Info.HasKeywordContaining("CP") && !string.IsNullOrWhiteSpace(x.Info.Trigger)),
            "公开手牌中最多1张斯巴达姆以外的CP卡并发动其触发", "RevealOwnHand");
        if (reveal.Count != 1 || !me.Hand.Contains(reveal[0]) || reveal[0].MatchesName("斯巴达姆") || !reveal[0].Info.HasKeywordContaining("CP")) return;
        c.BroadcastReveal(reveal[0]);
        await EffectRuntime.Resolve(c.State, c.OwnerIndex, reveal[0], EffectTrigger.OnLifeRevealTrigger, c.Prompts, lifeTriggerOrigin: true);
    }
    private static Task C110(EffectContext c)
        => October29Effects.SearchTop(c, 4, x => !x.MatchesName("布鲁诺") && x.Info.HasKeywordContaining("CP"));

    private static async Task ReturnCharacterCost(EffectContext c)
    {
        var me = October29Effects.Me(c);
        var cost = await October29Effects.Pick(c, me.Characters, "可以将我方1张角色放回手牌作为成本，或取消发动");
        if (cost.Count != 1 || !me.Characters.Contains(cost[0])) return;
        bool previous = EffectRuntime.PayingCost; EffectRuntime.PayingCost = true;
        try { AtomicOps.BounceToHand(c.State, c.OwnerIndex, cost[0]); }
        finally { EffectRuntime.PayingCost = previous; }
        if (me.Characters.Contains(cost[0]) || !me.Hand.Contains(cost[0])) return;
        await October29Effects.Move(c, 4, "deckBottom", anySide: true);
    }

    private static async Task Life(EffectContext c)
    {
        var me = October29Effects.Me(c);
        switch (c.Source.Info.Number)
        {
            case "OP18-038": case "OP18-094": await AtomicOps.DrawAsync(c.State, c.OwnerIndex, 1); break;
            case "OP18-040": await October29Effects.Move(c, 7, "rest"); break;
            case "OP18-058": await ReturnCharacterCost(c); break;
            case "OP18-074":
                if (!await AtomicOps.PromptReturnDonToDeck(c, 1)) break;
                var stages = await October29Effects.Pick(c, me.Trash.Where(x => x.Info.Kind == CardKind.Stage), "将废弃区中最多2张舞台加入手牌", "OwnTrash", 0, 2);
                if (stages.All(x => me.Trash.Contains(x) && x.Info.Kind == CardKind.Stage)) foreach (var x in stages) AtomicOps.TrashToHand(me, x); break;
            case "OP18-081":
                bool Eligible(CardInstance x) => x.Info.Kind == CardKind.Character && x.Info.Cost <= 4 && x.Info.HasKeyword("恐怖之船海盗团");
                var revive = await October29Effects.Pick(c, me.Trash.Where(Eligible), "休息登场最多1张费用不高于4的恐怖之船角色", "OwnTrashCharacter");
                if (revive.Count == 1 && me.Trash.Contains(revive[0]) && Eligible(revive[0])) await AtomicOps.PlayFromTrashFree(c.State, c.OwnerIndex, revive[0], restState: true); break;
            case "OP18-095": await C095(c); break;
            case "OP18-099": case "OP18-115":
                if (!me.Leader.Info.HasKeywordContaining("CP")) break;
                await AtomicOps.DrawAsync(c.State, c.OwnerIndex, 1);
                if (!c.State.IsGameOver) await October29Effects.Move(c, c.Source.Info.Number == "OP18-099" ? 8 : 7, "deckBottom", anySide: true); break;
            case "OP18-105": await C105(c); break;
            case "OP18-107": case "OP18-114": if (me.Leader.Info.HasKeywordContaining("CP")) await October29Effects.Move(c, 5, "deckBottom"); break;
            case "OP18-109":
                if (!me.Leader.Info.HasKeywordContaining("CP")) break;
                int option = await c.Prompts.ChooseOption(c.OwnerIndex, "选择触发效果", ["KO对方最多1张费用不高于6的角色", "休息对方最多1张费用不高于6的角色"]);
                if (option is 0 or 1) await October29Effects.Move(c, 6, option == 0 ? "ko" : "rest"); break;
            case "OP18-110":
                await AtomicOps.DrawAsync(c.State, c.OwnerIndex, 1);
                if (!c.State.IsGameOver) await October29Effects.Move(c, 2, "ko"); break;
            case "OP18-116": if (me.Leader.Info.HasKeywordContaining("CP")) await October29Effects.Move(c, int.MaxValue, "rest"); break;
        }
    }

    private static async Task BusterCall(EffectContext c)
    {
        var me = October29Effects.Me(c); var s = c.State;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将我方全部生命放置到废弃区作为成本，发动屠魔令？")) return;
        while (me.LifeArea.Count > 0) OfficialCoverageHelpers.TrashLifeTop(s, c.OwnerIndex);
        if (!me.Leader.MatchesName("斯巴达姆")) return;
        var snapshots = s.Players.Select(p => p.Characters.ToList()).ToList();
        for (int side = 0; side < 2; side++)
            await AtomicOps.ProcessEffectLeavesAsync(s, side, snapshots[side], c.Prompts, "trash", (state, owner, card) => AtomicOps.TrashFieldCard(state, owner, card));
        if (s.IsGameOver) return;
        bool Eligible(CardInstance x) => x.Info.Kind == CardKind.Character && x.Info.HasKeyword("中将");
        var deckCards = me.Deck.Where(Eligible).ToList(); var trashCards = me.Trash.Where(Eligible).ToList();
        var picked = await October29Effects.Pick(c, deckCards.Concat(trashCards), "从卡组或废弃区登场最多5张名称不同的中将角色", "OwnDeckOrTrashCharacter", 0, 5);
        bool valid = picked.Select(x => x.Info.Name).Distinct().Count() == picked.Count
            && picked.All(x => Eligible(x) && (deckCards.Contains(x) ? me.Deck.Contains(x) : me.Trash.Contains(x)));
        if (valid)
            foreach (var card in picked)
            {
                if (s.IsGameOver) break;
                if (deckCards.Contains(card)) await AtomicOps.PlayFromDeckFree(s, c.OwnerIndex, card);
                else await AtomicOps.PlayFromTrashFree(s, c.OwnerIndex, card);
            }
        if (c.Engine is not null) c.Engine.ShuffleDeck(me, c.OwnerIndex, "op18_117");
        else AtomicOps.Shuffle(s, me.Deck);
    }
}

public sealed class OP18_002_October29 : October29Card { public override string CardNumber => "OP18-002"; }
public sealed class OP18_004_October29 : October29Card { public override string CardNumber => "OP18-004"; }
public sealed class OP18_005_October29 : October29Card { public override string CardNumber => "OP18-005"; }
public sealed class OP18_006_October29 : October29Card { public override string CardNumber => "OP18-006"; }
public sealed class OP18_007_October29 : October29Card { public override string CardNumber => "OP18-007"; }
public sealed class OP18_008_October29 : October29Card { public override string CardNumber => "OP18-008"; }
public sealed class OP18_009_October29 : October29Card { public override string CardNumber => "OP18-009"; }
public sealed class OP18_012_October29 : October29Card { public override string CardNumber => "OP18-012"; }
public sealed class OP18_014_October29 : October29Card { public override string CardNumber => "OP18-014"; }
public sealed class OP18_019_October29 : October29Card { public override string CardNumber => "OP18-019"; }
public sealed class OP18_020_October29 : October29Card { public override string CardNumber => "OP18-020"; }
public sealed class OP18_023_October29 : October29Card { public override string CardNumber => "OP18-023"; }
public sealed class OP18_026_October29 : October29Card { public override string CardNumber => "OP18-026"; }
public sealed class OP18_027_October29 : October29Card { public override string CardNumber => "OP18-027"; }
public sealed class OP18_030_October29 : October29Card { public override string CardNumber => "OP18-030"; }
public sealed class OP18_033_October29 : October29Card { public override string CardNumber => "OP18-033"; }
public sealed class OP18_037_October29 : October29Card { public override string CardNumber => "OP18-037"; }
public sealed class OP18_038_October29 : October29Card { public override string CardNumber => "OP18-038"; }
public sealed class OP18_039_October29 : October29Card { public override string CardNumber => "OP18-039"; }
public sealed class OP18_040_October29 : October29Card { public override string CardNumber => "OP18-040"; }
public sealed class OP18_042_October29 : October29Card { public override string CardNumber => "OP18-042"; }
public sealed class OP18_045_October29 : October29Card { public override string CardNumber => "OP18-045"; }
public sealed class OP18_049_October29 : October29Card { public override string CardNumber => "OP18-049"; }
public sealed class OP18_050_October29 : October29Card { public override string CardNumber => "OP18-050"; }
public sealed class OP18_053_October29 : October29Card { public override string CardNumber => "OP18-053"; }
public sealed class OP18_054_October29 : October29Card { public override string CardNumber => "OP18-054"; }
public sealed class OP18_057_October29 : October29Card { public override string CardNumber => "OP18-057"; }
public sealed class OP18_058_October29 : October29Card { public override string CardNumber => "OP18-058"; }
public sealed class OP18_059_October29 : October29Card { public override string CardNumber => "OP18-059"; }
public sealed class OP18_067_October29 : October29Card { public override string CardNumber => "OP18-067"; }
public sealed class OP18_068_October29 : October29Card { public override string CardNumber => "OP18-068"; }
public sealed class OP18_071_October29 : October29Card { public override string CardNumber => "OP18-071"; }
public sealed class OP18_072_October29 : October29Card { public override string CardNumber => "OP18-072"; }
public sealed class OP18_074_October29 : October29Card { public override string CardNumber => "OP18-074"; }
public sealed class OP18_080_October29 : October29Card { public override string CardNumber => "OP18-080"; }
public sealed class OP18_081_October29 : October29Card { public override string CardNumber => "OP18-081"; }
public sealed class OP18_082_October29 : October29Card { public override string CardNumber => "OP18-082"; }
public sealed class OP18_083_October29 : October29Card { public override string CardNumber => "OP18-083"; }
public sealed class OP18_087_October29 : October29Card { public override string CardNumber => "OP18-087"; }
public sealed class OP18_088_October29 : October29Card { public override string CardNumber => "OP18-088"; }
public sealed class OP18_094_October29 : October29Card { public override string CardNumber => "OP18-094"; }
public sealed class OP18_095_October29 : October29Card { public override string CardNumber => "OP18-095"; }
public sealed class OP18_096_October29 : October29Card { public override string CardNumber => "OP18-096"; }
public sealed class OP18_097_October29 : October29Card { public override string CardNumber => "OP18-097"; }
public sealed class OP18_099_October29 : October29Card { public override string CardNumber => "OP18-099"; }
public sealed class OP18_105_October29 : October29Card { public override string CardNumber => "OP18-105"; }
public sealed class OP18_107_October29 : October29Card { public override string CardNumber => "OP18-107"; }
public sealed class OP18_109_October29 : October29Card { public override string CardNumber => "OP18-109"; }
public sealed class OP18_110_October29 : October29Card { public override string CardNumber => "OP18-110"; }
public sealed class OP18_114_October29 : October29Card { public override string CardNumber => "OP18-114"; }
public sealed class OP18_115_October29 : October29Card { public override string CardNumber => "OP18-115"; }
public sealed class OP18_116_October29 : October29Card { public override string CardNumber => "OP18-116"; }
public sealed class OP18_117_October29 : October29Card { public override string CardNumber => "OP18-117"; }
