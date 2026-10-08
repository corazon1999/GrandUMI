using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>2026-10-08 新卡的选择与复验；复用已验证的检索及咚!!成本结算。</summary>
internal static class OctoberCardHelpers
{
    public static Task<List<CardInstance>> Pick(EffectContext ctx, string kind, string text,
        IEnumerable<CardInstance> cards)
        => OP18EB05EffectHelpers.Pick(ctx, kind, text, cards, 0, 1);

    public static bool IsOwnFieldCard(EffectContext ctx, CardInstance card)
        => ctx.State.Players[ctx.OwnerIndex].Leader == card
            || ctx.State.Players[ctx.OwnerIndex].Characters.Contains(card);

    public static async Task RestOpponent(EffectContext ctx, bool includeLeader, int? maximumCost = null)
    {
        int side = 1 - ctx.OwnerIndex;
        var opponent = ctx.State.Players[side];
        var cards = opponent.Characters.AsEnumerable();
        if (includeLeader) cards = cards.Prepend(opponent.Leader);
        bool Eligible(CardInstance card) => !card.IsTapped && AtomicOps.CanRestCard(ctx.State, card, side)
            && (maximumCost is null || ctx.State.CurrentCostOf(side, card) <= maximumCost);
        var picked = await Pick(ctx, "OppRestTarget", "将对方最多1张符合条件的卡牌转为休息状态", cards.Where(Eligible));
        if (picked.Count == 1 && (opponent.Leader == picked[0] || opponent.Characters.Contains(picked[0]))
            && Eligible(picked[0])) AtomicOps.RestCard(picked[0]);
    }

    public static async Task MoveCharacter(EffectContext ctx, int maximumCost, bool toDeckBottom)
    {
        var targets = ctx.State.Players.SelectMany((player, side) =>
            player.Characters.Where(card => ctx.State.CurrentCostOf(side, card) <= maximumCost)
                .Select(card => (side, card))).ToList();
        var picked = await Pick(ctx, "AnyCharacter", toDeckBottom
            ? $"将最多1张费用不高于{maximumCost}的角色放回其持有者的卡组最下方"
            : $"将最多1张费用不高于{maximumCost}的角色放回其持有者的手牌", targets.Select(target => target.card));
        if (picked.Count != 1) return;
        var target = targets.First(item => item.card == picked[0]);
        if (!ctx.State.Players[target.side].Characters.Contains(target.card)
            || ctx.State.CurrentCostOf(target.side, target.card) > maximumCost) return;
        if (await AtomicOps.TryEffectLeaveGuard(ctx.State, target.side, target.card, ctx.Prompts,
            toDeckBottom ? "deckBottom" : "hand")) return;
        if (!ctx.State.Players[target.side].Characters.Contains(target.card)
            || ctx.State.CurrentCostOf(target.side, target.card) > maximumCost) return;
        if (toDeckBottom) AtomicOps.ReturnFieldToDeckBottom(ctx.State, target.side, target.card);
        else AtomicOps.BounceToHand(ctx.State, target.side, target.card);
    }
}

/// <summary>薇薇：双特征角色持续加攻；仅对方回合自身加6000并获得阻挡者。</summary>
public sealed class OP18_011_Vivi : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP18-011";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public Task Resolve(EffectContext ctx) => Task.CompletedTask;
    public Task RegisterFieldStatic(EffectContext ctx)
    {
        string id = ctx.Source.Id.ToString();
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == id);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = id,
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
            PowerDelta = 1000,
            Predicate = (_, side, card) => side == owner && card.Info.Kind == CardKind.Character
                && card.Info.HasKeyword("阿拉巴斯坦王国") && card.Info.HasKeyword("动物"),
        });
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = id,
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false },
            PowerDelta = 6000,
            GrantKeyword = "阻挡者",
            Predicate = (state, side, card) => side == owner && card.Id == ctx.Source.Id
                && state.CurrentTurnPlayer != owner,
        });
        return Task.CompletedTask;
    }
}

/// <summary>可可罗：回合结束时可废弃自身作为成本，重置我方领袖；不按 KO 结算。</summary>
public sealed class OP18_024_Kokoro : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-024";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnMyTurnEnd;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger != EffectTrigger.OnMyTurnEnd || (state.CurrentTurnPlayer == owner
            && state.Players[owner].Characters.Contains(source));
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnMyTurnEnd
            || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将可可罗放置到废弃区，将我方领袖转为活跃状态？")) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Characters.Contains(ctx.Source)) return;
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        try { AtomicOps.TrashFieldCard(ctx.State, ctx.OwnerIndex, ctx.Source, ignoreEffectLeaveGuard: true); }
        finally { EffectRuntime.PayingCost = previous; }
        if (me.Trash.Contains(ctx.Source)) AtomicOps.ActivateCard(me.Leader);
    }
}

/// <summary>弗兰奇：登场手牌角色或舞台，再按此时的舞台费用检查休息对方角色。</summary>
public sealed class OP18_034_Franky : IScriptedEffect
{
    public string CardNumber => "OP18-034";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool Eligible(CardInstance card) => card.Info.Kind is CardKind.Character or CardKind.Stage
            && ctx.State.CurrentCostOf(ctx.OwnerIndex, card) <= 5
            && (card.Info.HasKeyword("弗兰奇一家") || card.Info.HasKeyword("草帽一伙"));
        var picked = await OctoberCardHelpers.Pick(ctx, "OwnHandCard",
            "登场手牌中最多1张费用不高于5的《弗兰奇一家》或《草帽一伙》角色或舞台", me.Hand.Where(Eligible));
        if (picked.Count == 1 && me.Hand.Contains(picked[0]) && Eligible(picked[0]))
            await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked[0]);
        if (!ctx.State.IsGameOver && me.StageCards.Any(card => ctx.State.CurrentCostOf(ctx.OwnerIndex, card) >= 5))
            await OctoberCardHelpers.RestOpponent(ctx, includeLeader: false, maximumCost: 6);
    }
}

/// <summary>Mr.0与Miss.全周日：抽2后给领袖及每个角色各至多2张休息咚；攻击时弃1保护领袖。</summary>
public sealed class OP18_046_Mr0AllSunday : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-046";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnOppAttackDeclare;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger != EffectTrigger.OnOppAttackDeclare || (state.CurrentTurnPlayer != owner
            && state.Players[owner].Hand.Count > 0
            && state.Players[owner].Leader.Info.HasKeywordContaining("巴洛克工作室"));
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);
            if (ctx.State.IsGameOver) return;
            var targets = new[] { me.Leader }.Concat(me.Characters).ToList();
            for (int index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                int maximum = Math.Min(2, me.CostArea.Count(don => don.State == DonState.Rest));
                if (maximum == 0) break;
                if (!OctoberCardHelpers.IsOwnFieldCard(ctx, target)) continue;
                string position = index == 0 ? "领袖" : $"第{index}张角色";
                int count = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                    $"为我方{position}「{target.Info.Name}」赋予最多2张休息状态的咚!!",
                    Enumerable.Range(0, maximum + 1).Select(value => $"{value} 张").ToList());
                if (count < 0 || count > maximum || !OctoberCardHelpers.IsOwnFieldCard(ctx, target)
                    || me.CostArea.Count(don => don.State == DonState.Rest) < count) continue;
                AtomicOps.AttachDonFromCost(me, target.Id, count, DonState.Rest);
            }
            return;
        }
        if (ctx.Trigger != EffectTrigger.OnOppAttackDeclare
            || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "丢弃1张手牌，本回合将巴洛克工作室领袖的原本力量变为7000？")) return;
        var cards = await OP18EB05EffectHelpers.Pick(ctx, "OwnHandDiscard",
            "选择丢弃的1张手牌作为成本", me.Hand, 1, 1);
        if (cards.Count != 1 || !me.Hand.Contains(cards[0])
            || !me.Leader.Info.HasKeywordContaining("巴洛克工作室")) return;
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        try { AtomicOps.DiscardHand(me, cards[0]); }
        finally { EffectRuntime.PayingCost = previous; }
        me.Leader.OriginalPowerOverride = 7000;
    }
}

/// <summary>Mr.1与Miss.双指：速攻由基础能力处理，登场费用不高于5的巴洛克工作室角色。</summary>
public sealed class OP18_048_Mr1DoubleFinger : IScriptedEffect
{
    public string CardNumber => "OP18-048";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool Eligible(CardInstance card) => card.Info.Kind == CardKind.Character
            && ctx.State.CurrentCostOf(ctx.OwnerIndex, card) <= 5
            && card.Info.HasKeywordContaining("巴洛克工作室");
        var picked = await OctoberCardHelpers.Pick(ctx, "OwnHandCharacter",
            "登场最多1张费用不高于5且特征包含《巴洛克工作室》的手牌角色", me.Hand.Where(Eligible));
        if (picked.Count == 1 && me.Hand.Contains(picked[0]) && Eligible(picked[0]))
            await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked[0]);
    }
}

/// <summary>阿斯巴古：检索顶5中的舞台或七水之城卡，合计至多2张，其余自选顺序沉底。</summary>
public sealed class OP18_061_Iceburg : IScriptedEffect
{
    public string CardNumber => "OP18-061";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public Task Resolve(EffectContext ctx) => ctx.Trigger != EffectTrigger.OnEnterField ? Task.CompletedTask
        : EB05UpdatedEffects.SearchTop(ctx, 5, card => card.Info.Kind == CardKind.Stage
            || card.Info.HasKeyword("七水之城"), 2, "公开最多2张舞台或《七水之城》卡牌并加入手牌", trashRemainder: false);
}

/// <summary>军子宫：检索顶4并废弃剩余；休息4咚后从废弃区登场神之骑士团角色。</summary>
public sealed class OP18_084_Gunko : IScriptedEffect, IActivatedMainAvailability
{
    public string CardNumber => "OP18-084";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnEnterField or EffectTrigger.ActivatedMain;
    public string? GetActivatedMainUnavailableReason(GameState state, int owner, CardInstance source)
        => state.Players[owner].CostArea.Count(don => don.State == DonState.Active) < 4
            ? "需要4张活跃状态的咚!!" : null;
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            await EB05UpdatedEffects.SearchTop(ctx, 4, card => card.Info.HasKeyword("神之骑士团"),
                1, "公开最多1张《神之骑士团》卡牌加入手牌，其余放置到废弃区", trashRemainder: true);
            return;
        }
        if (ctx.Trigger != EffectTrigger.ActivatedMain || ctx.State.CurrentTurnPlayer != ctx.OwnerIndex
            || GetActivatedMainUnavailableReason(ctx.State, ctx.OwnerIndex, ctx.Source) is not null) return;
        if (!await EB05UpdatedEffects.RestDonCost(ctx, 4, "选择4张活跃咚!!作为成本，或取消发动")) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool Eligible(CardInstance card) => card.Info.Kind == CardKind.Character
            && ctx.State.CurrentCostOf(ctx.OwnerIndex, card) <= 6 && card.Info.HasKeyword("神之骑士团");
        var picked = await OctoberCardHelpers.Pick(ctx, "OwnTrashCharacter",
            "登场废弃区中最多1张费用不高于6的《神之骑士团》角色", me.Trash.Where(Eligible));
        if (picked.Count == 1 && me.Trash.Contains(picked[0]) && Eligible(picked[0]))
            await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked[0]);
    }
}

/// <summary>佳妮法：抽1后可正面加入CP生命卡；CP领袖生命触发抽1并休息对方领袖或角色。</summary>
public sealed class OP18_100_Kalifa : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-100";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnLifeRevealTrigger;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger != EffectTrigger.OnLifeRevealTrigger || state.Players[owner].Leader.Info.HasKeywordContaining("CP");
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger)
            || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        if (ctx.State.IsGameOver) return;
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            await OctoberCardHelpers.RestOpponent(ctx, includeLeader: true);
            return;
        }
        var picked = await OctoberCardHelpers.Pick(ctx, "OwnHandLife", "将手牌中最多1张特征包含CP的卡牌正面加入生命区",
            me.Hand.Where(card => card.Info.HasKeywordContaining("CP")));
        if (picked.Count != 1) return;
        int position = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择正面生命卡放置的位置", ["生命区最上方", "生命区最下方"]);
        if (position is not (0 or 1) || !me.Hand.Contains(picked[0]) || !picked[0].Info.HasKeywordContaining("CP")) return;
        AtomicOps.HandToLife(me, picked[0], toTop: position == 0, faceUp: true);
    }
}

/// <summary>罗布·鲁兹：登场/KO废弃对方顶部生命；CP领袖触发抽1并将任意一方低费用角色沉底。</summary>
public sealed class OP18_113_RobLucci : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-113";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnKO or EffectTrigger.OnLifeRevealTrigger;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger != EffectTrigger.OnLifeRevealTrigger || state.Players[owner].Leader.Info.HasKeywordContaining("CP");
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger)
            || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        if (ctx.Trigger is EffectTrigger.OnEnterField or EffectTrigger.OnKO)
        {
            OfficialCoverageHelpers.TrashLifeTop(ctx.State, 1 - ctx.OwnerIndex);
            return;
        }
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        if (!ctx.State.IsGameOver) await OctoberCardHelpers.MoveCharacter(ctx, 8, toDeckBottom: true);
    }
}

/// <summary>大家要保重喔：奈美领袖主要效果退回任一方角色；反击抽1并使我方一张卡加1000。</summary>
public sealed class EB05_030_TakeCare : IScriptedEffect
{
    public string CardNumber => "EB05-030";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.EventMain or EffectTrigger.EventCounter;
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            if (me.Leader.MatchesName("奈美")) await OctoberCardHelpers.MoveCharacter(ctx, 5, toDeckBottom: false);
            return;
        }
        if (ctx.Trigger != EffectTrigger.EventCounter) return;
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        if (ctx.State.IsGameOver) return;
        var picked = await OctoberCardHelpers.Pick(ctx, "OwnPowerTarget", "本回合我方最多1张领袖或角色力量+1000",
            new[] { me.Leader }.Concat(me.Characters));
        if (picked.Count == 1 && OctoberCardHelpers.IsOwnFieldCard(ctx, picked[0])) AtomicOps.AddPowerThisTurn(picked[0], 1000);
    }
}
