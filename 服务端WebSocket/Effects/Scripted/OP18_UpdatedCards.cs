using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>OP18 后续公布卡牌的成本校验与 KO 事件快照读取。</summary>
internal static class OP18UpdatedHelpers
{
    public static bool CanRest(EffectContext ctx, CardInstance card)
        => !card.IsTapped && AtomicOps.CanRestCard(ctx.State, card);

    public static bool WasOwnCharacterKO(GameState state, int owner,
        IReadOnlyDictionary<string, object?>? payload, string feature, int minimumPower = 0)
    {
        if (payload is null || !payload.TryGetValue("owner", out var rawOwner)
            || rawOwner is not int koOwner || koOwner != owner) return false;
        var id = payload.TryGetValue("cardId", out var rawId) ? rawId as string : null;
        var card = OP18EB05EffectHelpers.FindOwnedCard(state.Players[owner], id);
        var kind = payload.TryGetValue("cardKind", out var rawKind) ? rawKind as string : card?.Info.Kind.ToString();
        if (kind != nameof(CardKind.Character)) return false;
        bool matches = payload.TryGetValue("cardKeywords", out var rawKeywords) && rawKeywords is string[] keywords
            ? keywords.Any(keyword => KeywordNormalizer.Equals(keyword, feature))
            : card?.Info.HasKeyword(feature) == true;
        int power = payload.TryGetValue("originalPower", out var rawPower) && rawPower is int originalPower
            ? originalPower : card is null ? 0 : state.OriginalPowerOf(owner, card);
        return matches && power >= minimumPower;
    }

    public static async Task DiscardOpponent(EffectContext ctx)
    {
        var opponent = ctx.State.Players[1 - ctx.OwnerIndex];
        var cards = opponent.Hand.ToList();
        if (cards.Count == 0 || ctx.State.IsGameOver) return;
        var answer = await ctx.Prompts.ChooseCards(1 - ctx.OwnerIndex, "OwnHandDiscard",
            "选择丢弃1张手牌", cards.Select(card => card.Id.ToString()).ToList(), 1, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = cards.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        if (answer.Count != 1) return;
        var selected = cards.FirstOrDefault(card => card.Id.ToString() == answer[0]);
        if (selected is not null && opponent.Hand.Contains(selected)) AtomicOps.DiscardHand(opponent, selected);
    }
}

/// <summary>卡鲁：双特征角色持续加成；我方阿拉巴斯坦角色被 KO 时每回合抽1。</summary>
public sealed class OP18_001_Karoo : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-001";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnGameStart or EffectTrigger.OnAnyCharKOd;
    private static string Key(CardInstance source) => $"OP18-001-ko:{source.Id}";

    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger != EffectTrigger.OnAnyCharKOd || (!state.Players[owner].TurnOnceUsed.Contains(Key(source))
            && OP18UpdatedHelpers.WasOwnCharacterKO(state, owner, payload, "阿拉巴斯坦王国"));

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnGameStart)
        {
            int owner = ctx.OwnerIndex;
            ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == ctx.Source.Id.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = ctx.Source.Id.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
                PowerDelta = 1000,
                GrantKeyword = "速攻",
                Predicate = (_, side, card) => side == owner && card.Info.Kind == CardKind.Character
                    && card.Info.HasKeyword("动物") && card.Info.HasKeyword("阿拉巴斯坦王国"),
            });
            return;
        }
        if (ctx.Trigger != EffectTrigger.OnAnyCharKOd
            || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        ctx.State.Players[ctx.OwnerIndex].TurnOnceUsed.Add(Key(ctx.Source));
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
    }
}

/// <summary>路飞：流放由基础能力处理；KO 时登场手牌中的低力量阿拉巴斯坦角色。</summary>
public sealed class OP18_016_Luffy : IScriptedEffect
{
    public string CardNumber => "OP18-016";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnKO;
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnKO) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        var cards = me.Hand.Where(card => card.Info.Kind == CardKind.Character
            && card.Info.Power <= 6000 && card.Info.HasKeyword("阿拉巴斯坦王国"));
        var picked = await OP18EB05EffectHelpers.Pick(ctx, "OwnHandCharacter",
            "将手牌中最多1张力量不高于6000的《阿拉巴斯坦王国》角色登场", cards, 0, 1);
        if (picked.Count == 1 && me.Hand.Contains(picked[0]))
            await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked[0]);
    }
}

/// <summary>路飞：咚×3攻击时每回合一次重置自身，并跳过下个我方重置阶段的重置。</summary>
public sealed class OP18_022_Luffy : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-022";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnAttackDeclare;
    private static string Key(CardInstance source) => $"OP18-022-attack:{source.Id}";
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger != EffectTrigger.OnAttackDeclare || (state.CurrentTurnPlayer == owner
            && source.IsTapped && state.Players[owner].AttachedDonCount(source.Id) >= 3
            && !state.Players[owner].TurnOnceUsed.Contains(Key(source)));
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnAttackDeclare
            || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "路飞：将此领袖转为活跃状态，并跳过下个我方重置阶段的重置？")) return;
        AtomicOps.ActivateCard(ctx.Source);
        ctx.Source.CannotActivateNextReset = true;
        ctx.State.Players[ctx.OwnerIndex].TurnOnceUsed.Add(Key(ctx.Source));
    }
}

/// <summary>昆平：我方回合结束时，仅自身休息的场合重置领袖。</summary>
public sealed class OP18_025_Gonbe : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-025";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnMyTurnEnd;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger != EffectTrigger.OnMyTurnEnd || (state.CurrentTurnPlayer == owner && source.IsTapped);
    public Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnMyTurnEnd
            && IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars))
            AtomicOps.ActivateCard(ctx.State.Players[ctx.OwnerIndex].Leader);
        return Task.CompletedTask;
    }
}

/// <summary>奇姆尼：休息自身和昆平作为成本，重置路飞领袖。</summary>
public sealed class OP18_028_Chimney : IScriptedEffect, IActivatedMainAvailability
{
    public string CardNumber => "OP18-028";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;
    public string? GetActivatedMainUnavailableReason(GameState state, int owner, CardInstance source)
    {
        var me = state.Players[owner];
        if (!me.Leader.MatchesName("蒙奇·D·路飞")) return "领袖必须为蒙奇·D·路飞";
        if (source.IsTapped || !AtomicOps.CanRestCard(state, source)) return "此角色必须能够转为休息状态";
        if (!me.Characters.Any(card => card.Id != source.Id && card.MatchesName("昆平")
            && !card.IsTapped && AtomicOps.CanRestCard(state, card))) return "需要能够转为休息状态的昆平";
        return null;
    }
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.ActivatedMain || ctx.State.CurrentTurnPlayer != ctx.OwnerIndex
            || GetActivatedMainUnavailableReason(ctx.State, ctx.OwnerIndex, ctx.Source) is not null) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "休息奇姆尼和1张昆平，将路飞领袖转为活跃状态？")) return;
        var picked = await OP18EB05EffectHelpers.Pick(ctx, "OwnCharacter", "选择转为休息状态的昆平",
            me.Characters.Where(card => card.Id != ctx.Source.Id && card.MatchesName("昆平") && OP18UpdatedHelpers.CanRest(ctx, card)), 1, 1);
        if (picked.Count != 1 || !me.Characters.Contains(ctx.Source) || !me.Characters.Contains(picked[0])
            || !OP18UpdatedHelpers.CanRest(ctx, ctx.Source) || !OP18UpdatedHelpers.CanRest(ctx, picked[0])) return;
        AtomicOps.RestCard(ctx.Source);
        AtomicOps.RestCard(picked[0]);
        AtomicOps.ActivateCard(me.Leader);
    }
}

/// <summary>Miss.全周日：符合特征及原本力量的我方角色被 KO 时抽1、对方弃1。</summary>
public sealed class OP18_041_MissAllSunday : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-041";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnAnyCharKOd;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger != EffectTrigger.OnAnyCharKOd || OP18UpdatedHelpers.WasOwnCharacterKO(state, owner, payload, "巴洛克工作室", 3000);
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnAnyCharKOd
            || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        await OP18UpdatedHelpers.DiscardOpponent(ctx);
    }
}

/// <summary>咚×1的额外力量加成独立于登场时效果，整卡无效时由持续效果门禁停用。</summary>
public abstract class OP18DonPowerEffect : IScriptedEffect, IFieldStaticEffect
{
    public abstract string CardNumber { get; }
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public Task RegisterFieldStatic(EffectContext ctx)
    {
        var id = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == id.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
            PowerDelta = 1000,
            Predicate = (state, side, card) => side == owner && card.Id == id
                && state.Players[owner].AttachedDonCount(id) >= 1,
        });
        return Task.CompletedTask;
    }
    public abstract Task Resolve(EffectContext ctx);
}

public sealed class OP18_044_BeansAndCatherine : OP18DonPowerEffect
{
    public override string CardNumber => "OP18-044";
    public override async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        if (!ctx.State.IsGameOver && ctx.State.Players[ctx.OwnerIndex].Hand.Count > 0)
            await OP18EB05EffectHelpers.DiscardOne(ctx, "抽取1张卡牌后，丢弃1张手牌", isCost: false);
    }
}

public sealed class OP18_056_Unluckies : OP18DonPowerEffect
{
    public override string CardNumber => "OP18-056";
    public override async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        var candidates = me.Characters.Where(card => card.Id != ctx.Source.Id && card.Info.HasKeyword("巴洛克工作室")
            && !ctx.State.IsLeaveGuarded(card, "effect")).ToList();
        if (candidates.Count == 0 || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "将另一张《巴洛克工作室》角色放回卡组最下方，使对方丢弃1张手牌？")) return;
        var picked = await OP18EB05EffectHelpers.Pick(ctx, "OwnCharacter", "选择放回卡组最下方的角色", candidates, 1, 1);
        if (picked.Count != 1 || !me.Characters.Contains(picked[0]) || picked[0].Id == ctx.Source.Id
            || !picked[0].Info.HasKeyword("巴洛克工作室") || ctx.State.IsLeaveGuarded(picked[0], "effect")) return;
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        try { AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, picked[0]); }
        finally { EffectRuntime.PayingCost = previous; }
        if (me.Characters.Contains(picked[0])) return;
        await OP18UpdatedHelpers.DiscardOpponent(ctx);
    }
}

/// <summary>赞巴：KO低费用舞台作为成本，获得本回合速攻。</summary>
public sealed class OP18_066_Zambai : IScriptedEffect, IActivatedMainAvailability
{
    public string CardNumber => "OP18-066";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;
    public string? GetActivatedMainUnavailableReason(GameState state, int owner, CardInstance source)
        => state.Players[owner].StageCards.Any(card => state.CurrentCostOf(card) <= 5)
            ? null : "需要费用不高于5的舞台";
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.ActivatedMain || ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        var candidates = me.StageCards.Where(card => ctx.State.CurrentCostOf(card) <= 5).ToList();
        if (candidates.Count == 0 || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "KO我方1张费用不高于5的舞台，使赞巴本回合获得速攻？")) return;
        var picked = await OP18EB05EffectHelpers.Pick(ctx, "OwnStage", "选择作为成本KO的舞台", candidates, 1, 1);
        if (picked.Count != 1 || !me.StageCards.Contains(picked[0]) || ctx.State.CurrentCostOf(picked[0]) > 5) return;
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        bool paid;
        try { paid = await AtomicOps.KOByEffectAsync(ctx.State, ctx.OwnerIndex, picked[0], ctx.Prompts, ctx.OwnerIndex, deferOnKO: true); }
        finally { EffectRuntime.PayingCost = previous; }
        if (paid) AtomicOps.GiveKeyword(ctx.Source, "速攻", KeywordDuration.ThisTurn);
    }
}

/// <summary>鲨鱼潜水艇3号：登场抽牌/加咚；对方攻击时休息舞台，给两角色及合条件领袖加力量。</summary>
public sealed class OP18_076_SharkSubmerge : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-076";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnOppAttackDeclare;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger != EffectTrigger.OnOppAttackDeclare || (!source.IsTapped && AtomicOps.CanRestCard(state, source)
            && state.CurrentTurnPlayer != owner && state.Players[owner].StageCards.Contains(source));
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
            if (!ctx.State.IsGameOver && me.DonDeck.Count > 0
                && me.CostArea.Count < ctx.State.MaxDonInCostAreaFor(ctx.OwnerIndex))
            {
                int option = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                    "是否从咚!!卡组追加1张休息咚!!？",
                    ["不追加", "追加1张休息咚!!"]);
                if (option == 1 && me.DonDeck.Count > 0
                    && me.CostArea.Count < ctx.State.MaxDonInCostAreaFor(ctx.OwnerIndex))
                    AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
            }
            return;
        }
        if (ctx.Trigger != EffectTrigger.OnOppAttackDeclare
            || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "休息此舞台，使最多2张角色和力量不高于5000的领袖本回合力量+1000？")) return;
        if (!OP18UpdatedHelpers.CanRest(ctx, ctx.Source) || !me.StageCards.Contains(ctx.Source)) return;
        AtomicOps.RestCard(ctx.Source);
        // 门槛在加成前判断；角色不受5000门槛限制。
        bool buffsLeader = ctx.State.CurrentPowerOf(ctx.OwnerIndex, me.Leader) <= 5000;
        var picked = await OP18EB05EffectHelpers.Pick(ctx, "OwnCharacter", "选择最多2张角色，本回合力量+1000", me.Characters, 0, 2);
        foreach (var card in picked.Where(me.Characters.Contains)) AtomicOps.AddPowerThisTurn(card, 1000);
        if (buffsLeader) AtomicOps.AddPowerThisTurn(me.Leader, 1000);
    }
}

/// <summary>斯巴达姆：休息2咚、自身并弃CP卡的复合成本必须完整校验后一次支付。</summary>
public sealed class OP18_079_Spandam : IScriptedEffect, IActivatedMainAvailability
{
    public string CardNumber => "OP18-079";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;
    public string? GetActivatedMainUnavailableReason(GameState state, int owner, CardInstance source)
    {
        var me = state.Players[owner];
        if (source.IsTapped || !AtomicOps.CanRestCard(state, source)) return "此领袖必须能够转为休息状态";
        if (me.CostArea.Count(don => don.State == DonState.Active) < 2) return "需要2张活跃咚!!";
        return me.Hand.Any(card => card.Info.HasKeywordContaining("CP")) ? null : "需要1张拥有《CP》特征的手牌";
    }
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.ActivatedMain || ctx.State.CurrentTurnPlayer != ctx.OwnerIndex
            || GetActivatedMainUnavailableReason(ctx.State, ctx.OwnerIndex, ctx.Source) is not null) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "休息2张咚!!和此领袖，丢弃1张《CP》手牌，可将卡组顶最多1张加入生命顶？")) return;
        var dons = me.CostArea.Where(don => don.State == DonState.Active).Take(2).ToList();
        var picked = await OP18EB05EffectHelpers.Pick(ctx, "OwnHandDiscard", "选择作为成本丢弃的《CP》手牌",
            me.Hand.Where(card => card.Info.HasKeywordContaining("CP")), 1, 1);
        if (picked.Count != 1 || !me.Hand.Contains(picked[0]) || !picked[0].Info.HasKeywordContaining("CP")
            || dons.Count != 2 || dons.Any(don => !me.CostArea.Contains(don) || don.State != DonState.Active)
            || !OP18UpdatedHelpers.CanRest(ctx, ctx.Source)) return;
        if (!AtomicOps.RestCard(ctx.Source)) return;
        foreach (var don in dons) don.State = DonState.Rest;
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        try { AtomicOps.DiscardHand(me, picked[0]); }
        finally { EffectRuntime.PayingCost = previous; }
        if (me.Deck.Count > 0)
        {
            int option = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "是否将卡组最上方1张卡牌加入生命区最上方？",
                ["不加入", "加入生命区最上方"]);
            if (option == 1 && me.Deck.Count > 0)
                AtomicOps.AddLifeFromDeckTop(me, 1);
        }
    }
}

/// <summary>戈尔德巴古：持续费用+12；KO时KO对方低费用角色。</summary>
public sealed class OP18_086_Goldberg : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP18-086";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnKO;
    public Task RegisterFieldStatic(EffectContext ctx)
    {
        var id = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == id.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
            CostDelta = 12,
            Predicate = (_, side, card) => side == owner && card.Id == id,
        });
        return Task.CompletedTask;
    }
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnKO) return;
        var opponent = ctx.State.Players[1 - ctx.OwnerIndex];
        var picked = await OP18EB05EffectHelpers.Pick(ctx, "OpponentCharacter", "KO对方最多1张费用不高于4的角色",
            opponent.Characters.Where(card => ctx.State.CurrentCostOf(card) <= 4), 0, 1);
        if (picked.Count == 1 && opponent.Characters.Contains(picked[0]) && ctx.State.CurrentCostOf(picked[0]) <= 4)
            await AtomicOps.KOByEffectAsync(ctx.State, 1 - ctx.OwnerIndex, picked[0], ctx.Prompts, ctx.OwnerIndex);
    }
}
