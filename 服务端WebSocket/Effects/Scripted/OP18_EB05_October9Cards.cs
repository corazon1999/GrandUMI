using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>2026-10-09 新卡的严格选择与成本复验。</summary>
internal static class October9CardHelpers
{
    public static async Task<List<CardInstance>> Pick(EffectContext ctx, int side, string kind,
        string text, IEnumerable<CardInstance> source, int min = 0, int max = 1)
    {
        var cards = source.DistinctBy(card => card.Id).ToList();
        if (cards.Count == 0) return [];
        max = Math.Min(max, cards.Count);
        min = Math.Min(min, max);
        var answer = await ctx.Prompts.ChooseCards(side, kind, text,
            cards.Select(card => card.Id.ToString()).ToList(), min, max,
            new Dictionary<string, object?> { ["choiceCards"] = cards.Select(card =>
                new { id = card.Id.ToString(), number = card.Info.Number }).ToList() });
        if (answer.Count < min || answer.Count > max || answer.Distinct().Count() != answer.Count
            || answer.Any(id => cards.All(card => card.Id.ToString() != id))) return [];
        return answer.Select(id => cards.First(card => card.Id.ToString() == id)).ToList();
    }

    public static async Task<bool> Discard(EffectContext ctx, int side, int count, bool isCost)
    {
        var player = ctx.State.Players[side];
        if (isCost && player.Hand.Count < count) return false;
        int required = Math.Min(count, player.Hand.Count);
        if (required == 0) return !isCost;
        var picked = await Pick(ctx, side, "OwnHandDiscard", isCost
            ? $"可以丢弃{required}张手牌作为成本，或取消发动" : $"丢弃{required}张手牌",
            player.Hand, isCost ? 0 : required, required);
        if (picked.Count != required || picked.Any(card => !player.Hand.Contains(card))) return false;
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = isCost;
        try { foreach (var card in picked) AtomicOps.DiscardHand(player, card); }
        finally { EffectRuntime.PayingCost = previous; }
        return true;
    }

    public static CardInstance? Victim(GameState state, int owner, CardInstance source,
        EffectTrigger trigger, IReadOnlyDictionary<string, object?>? payload)
    {
        if (trigger == EffectTrigger.PreKO)
            return state.Players[owner].Characters.Contains(source) ? source : null;
        if (payload?.TryGetValue("victimId", out var value) != true) return null;
        string? id = value is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : value as string;
        return state.Players[owner].Characters.FirstOrDefault(card => card.Id.ToString() == id);
    }

    public static bool OpponentEffect(GameState state, int owner, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
    {
        if (trigger is EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd)
            return state.KOReason == "effect" && state.KOActingSide == 1 - owner;
        if (payload?.TryGetValue("actingSide", out var value) == true)
        {
            if (value is int side) return side == 1 - owner;
            if (value is JsonElement { ValueKind: JsonValueKind.Number } json && json.TryGetInt32(out side))
                return side == 1 - owner;
        }
        return EffectRuntime.CurrentActingSide == 1 - owner;
    }

    public static async Task BuffBattle(EffectContext ctx, Func<CardInstance, bool>? filter = null)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var picked = await Pick(ctx, ctx.OwnerIndex, "OwnLeaderOrCharacter", "使我方最多1张符合条件的领袖或角色本次战斗力量+4000",
            new[] { me.Leader }.Concat(me.Characters).Where(card => filter?.Invoke(card) ?? true));
        if (picked.Count == 1 && OctoberCardHelpers.IsOwnFieldCard(ctx, picked[0])
            && (filter?.Invoke(picked[0]) ?? true)) AtomicOps.AddPowerThisBattle(picked[0], 4000);
    }
}

/// <summary>佐罗：每回合一次以领袖减攻代替对方效果离场；攻击时赋予阿拉巴斯坦角色速攻。</summary>
public sealed class OP18_017_Zoro : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-017";
    private static string Once(CardInstance source) => $"OP18-017-leave:{source.Id}";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnAttackDeclare
        or EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger == EffectTrigger.OnAttackDeclare || (state.Players[owner].Characters.Contains(source)
            && !state.Players[owner].TurnOnceUsed.Contains(Once(source))
            && October9CardHelpers.OpponentEffect(state, owner, trigger, payload)
            && October9CardHelpers.Victim(state, owner, source, trigger, payload)?.Info.HasKeyword("阿拉巴斯坦王国") == true);
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnAttackDeclare)
        {
            var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnCharacter",
                "使我方最多1张《阿拉巴斯坦王国》角色本回合获得速攻", me.Characters.Where(card => card.Info.HasKeyword("阿拉巴斯坦王国")));
            if (picked.Count == 1 && me.Characters.Contains(picked[0]) && picked[0].Info.HasKeyword("阿拉巴斯坦王国"))
                AtomicOps.GiveKeyword(picked[0], "速攻", KeywordDuration.ThisTurn);
            return;
        }
        if (!HandlesTrigger(ctx.Trigger) || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "本回合将我方领袖力量-2000，使该角色不因对方效果离场？")
            || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        var victim = October9CardHelpers.Victim(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)!;
        AtomicOps.AddPowerThisTurn(me.Leader, -2000);
        me.TurnOnceUsed.Add(Once(ctx.Source));
        ctx.State.MarkPreventEffectLeaveBatch(ctx.OwnerIndex, victim.Id, card => card.Info.HasKeyword("阿拉巴斯坦王国"),
            isKoReplacement: ctx.Trigger is EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd);
    }
}

/// <summary>Mr.9与Miss.星期三：有赋予咚时持续加1000；登场退回对方费用不高于2的角色。</summary>
public sealed class OP18_055_Mr9Wednesday : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "OP18-055";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public Task RegisterFieldStatic(EffectContext ctx)
    {
        string id = ctx.Source.Id.ToString();
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == id);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect { SourceCardId = id,
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false }, PowerDelta = 1000,
            Predicate = (state, side, card) => side == owner && card == ctx.Source
                && state.Players[owner].CostArea.Any(don => don.State == DonState.Attached && don.AttachedToCardId == card.Id) });
        return Task.CompletedTask;
    }
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        int side = 1 - ctx.OwnerIndex;
        var opponent = ctx.State.Players[side];
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OppCharacter", "将对方最多1张费用不高于2的角色放回手牌",
            opponent.Characters.Where(card => ctx.State.CurrentCostOf(side, card) <= 2));
        if (picked.Count != 1 || !opponent.Characters.Contains(picked[0]) || ctx.State.CurrentCostOf(side, picked[0]) > 2) return;
        if (await AtomicOps.TryEffectLeaveGuard(ctx.State, side, picked[0], ctx.Prompts, "hand")) return;
        if (opponent.Characters.Contains(picked[0]) && ctx.State.CurrentCostOf(side, picked[0]) <= 2)
            AtomicOps.BounceToHand(ctx.State, side, picked[0]);
    }
}

/// <summary>索德姆与哥摩拉：放回1咚并休息自身，代替弗兰奇一家角色的KO。</summary>
public sealed class OP18_069_SodomGomorrah : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "OP18-069";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd;
    public bool IsTriggerAvailable(GameState state, int owner, CardInstance source, EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => state.Players[owner].Characters.Contains(source) && !source.IsTapped && AtomicOps.CanRestCard(state, source, owner)
            && state.Players[owner].CostArea.Any(don => don.State is DonState.Active or DonState.Rest or DonState.Attached)
            && October9CardHelpers.Victim(state, owner, source, trigger, payload)?.Info.HasKeyword("弗兰奇一家") == true;
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger) || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "咚!!-1并将索德姆与哥摩拉休息，使该角色不被KO？")) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        var dons = me.CostArea.Where(don => don.State is DonState.Active or DonState.Rest or DonState.Attached).ToList();
        var answer = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReturnOwnDon", "选择放回咚!!卡组的1张咚，或取消发动",
            dons.Select(don => don.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?> { ["donChoices"] = AtomicOps.BuildDonPromptChoices(me, dons), ["canCancel"] = true });
        if (answer.Count != 1) return;
        var selected = dons.FirstOrDefault(don => don.Id.ToString() == answer[0]);
        if (selected is null || !me.CostArea.Contains(selected)
            || !IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)
            || selected.State is not (DonState.Active or DonState.Rest or DonState.Attached)) return;
        var victim = October9CardHelpers.Victim(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)!;
        // 两项成本完整复验后无等待提交，取消或过期响应不产生部分支付。
        int donIndex = me.CostArea.IndexOf(selected);
        var previousState = selected.State;
        var previousTarget = selected.AttachedToCardId;
        selected.State = DonState.InDeck;
        selected.AttachedToCardId = null;
        me.CostArea.Remove(selected);
        me.DonDeck.Add(selected);
        // 放回咚可能改变持续休息限制；第二项成本失败时恢复尚未广播的咚变更。
        if (!AtomicOps.RestCard(ctx.State, ctx.Source, ctx.OwnerIndex))
        {
            me.DonDeck.Remove(selected);
            selected.State = previousState;
            selected.AttachedToCardId = previousTarget;
            me.CostArea.Insert(donIndex, selected);
            return;
        }
        ctx.State.MarkPreventKO(victim.Id);
        if (ctx.State.SimultaneousKOVictimIds is { } victims)
            foreach (var card in me.Characters.Where(card => victims.Contains(card.Id) && card.Info.HasKeyword("弗兰奇一家")))
                ctx.State.MarkPreventKO(card.Id);
        EffectRuntime.NotifyWatcher(EffectTrigger.OnDonReturnedToDeck,
            new Dictionary<string, object?> { ["count"] = 1, ["owner"] = ctx.OwnerIndex });
    }
}

/// <summary>巨人角色的持续费用+12只作用于场上的自身。</summary>
public abstract class October9Giant : IScriptedEffect, IFieldStaticEffect
{
    public abstract string CardNumber { get; }
    public abstract bool HandlesTrigger(EffectTrigger trigger);
    public abstract Task Resolve(EffectContext ctx);
    public Task RegisterFieldStatic(EffectContext ctx)
    {
        string id = ctx.Source.Id.ToString();
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == id);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect { SourceCardId = id,
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false }, CostDelta = 12,
            Predicate = (_, side, card) => side == owner && card == ctx.Source });
        return Task.CompletedTask;
    }
}

/// <summary>多利：登场可弃1作为成本，然后由对方选择其必须丢弃的手牌。</summary>
public sealed class OP18_089_Dorry : October9Giant
{
    public override string CardNumber => "OP18-089";
    public override bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public override async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField && await October9CardHelpers.Discard(ctx, ctx.OwnerIndex, 1, isCost: true))
            await October9CardHelpers.Discard(ctx, 1 - ctx.OwnerIndex, 1, isCost: false);
    }
}

/// <summary>布洛基：攻击时可弃1，再回收废弃区费用不高于2的角色。</summary>
public sealed class OP18_091_Brogy : October9Giant
{
    public override string CardNumber => "OP18-091";
    public override bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnAttackDeclare;
    public override async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnAttackDeclare || !await October9CardHelpers.Discard(ctx, ctx.OwnerIndex, 1, isCost: true)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool Eligible(CardInstance card) => card.Info.Kind == CardKind.Character && ctx.State.CurrentCostOf(ctx.OwnerIndex, card) <= 2;
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnTrashCharacter", "将废弃区最多1张费用不高于2的角色加入手牌", me.Trash.Where(Eligible));
        if (picked.Count == 1 && me.Trash.Contains(picked[0]) && Eligible(picked[0])) AtomicOps.TrashToHand(me, picked[0]);
    }
}

/// <summary>路飞趁现在吧：赋活跃咚与公开两张红色事件是完整成本；反击使用独立条件。</summary>
public sealed class EB05_008_LuffyNow : IScriptedEffect
{
    public string CardNumber => "EB05-008";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.EventMain or EffectTrigger.EventCounter;
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            if (!me.Characters.Any(card => ctx.State.CurrentCostOf(ctx.OwnerIndex, card) >= 5))
                await October9CardHelpers.BuffBattle(ctx);
            return;
        }
        if (ctx.Trigger != EffectTrigger.EventMain) return;
        bool RedEvent(CardInstance card) => card.Info.Kind == CardKind.Event && card.Info.ColorList.Contains("红");
        var dons = me.CostArea.Where(don => don.State == DonState.Active).ToList();
        if (dons.Count == 0 || me.Hand.Count(RedEvent) < 2) return;
        var selected = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AttachOwnDon", "可以为我方领袖赋予1张活跃咚!!，再公开2张红色事件，或取消发动",
            dons.Select(don => don.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?> { ["donChoices"] = AtomicOps.BuildDonPromptChoices(me, dons), ["canCancel"] = true });
        if (selected.Count != 1) return;
        var don = dons.FirstOrDefault(item => item.Id.ToString() == selected[0]);
        if (don is null) return;
        var revealed = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "RevealOwnHand", "公开手牌中的2张红色事件卡牌，或取消发动", me.Hand.Where(RedEvent), 0, 2);
        if (revealed.Count != 2 || revealed.Any(card => !me.Hand.Contains(card) || !RedEvent(card))
            || !me.CostArea.Contains(don) || don.State != DonState.Active) return;
        don.State = DonState.Attached;
        don.AttachedToCardId = me.Leader.Id;
        ctx.BroadcastReveal(revealed);
        var opponent = ctx.State.Players[1 - ctx.OwnerIndex];
        var targets = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OppRestCharacter", "本回合使对方最多1张休息状态的角色力量-7000", opponent.Characters.Where(card => card.IsTapped));
        if (targets.Count == 1 && opponent.Characters.Contains(targets[0]) && targets[0].IsTapped)
            AtomicOps.AddPowerThisTurn(targets[0], -7000);
    }
}

/// <summary>和之国开国：休息我方卡牌（含活跃咚）作为反击成本；触发登场力量不高于6000的和之国角色。</summary>
public sealed class EB05_019_OpenWano : IScriptedEffect
{
    public string CardNumber => "EB05-019";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.EventCounter or EffectTrigger.OnLifeRevealTrigger;
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            if (await AtomicOps.PromptRestOwnCards(ctx, 1, "可以休息我方1张卡牌，使和之国领袖或角色本次战斗力量+4000", optional: true))
                await October9CardHelpers.BuffBattle(ctx, card => card.Info.HasKeyword("和之国"));
            return;
        }
        if (ctx.Trigger != EffectTrigger.OnLifeRevealTrigger || ctx.State.Players[1 - ctx.OwnerIndex].LifeArea.Count > 3) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool Eligible(CardInstance card) => card.Info.Kind == CardKind.Character && card.Info.HasKeyword("和之国")
            && ctx.State.CurrentPowerOf(ctx.OwnerIndex, card) <= 6000;
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnHandCharacter", "登场手牌中最多1张力量不高于6000的《和之国》角色", me.Hand.Where(Eligible));
        if (picked.Count == 1 && me.Hand.Contains(picked[0]) && Eligible(picked[0]))
            await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked[0]);
    }
}

/// <summary>斯皮德：咚-1后检索顶5的SMILE，再登场至多2张力量不高于5000的SMILE角色。</summary>
public sealed class EB05_035_Speed : IScriptedEffect
{
    public string CardNumber => "EB05-035";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField || !await AtomicOps.PromptReturnDonToDeck(ctx, 1, optional: true)) return;
        // 综合规则8-4-4-2允许隐蔽区按卡牌资料检索时选0；中文“2张”采用相同检索规则。
        await EB05UpdatedEffects.SearchTop(ctx, 5, card => card.Info.HasKeyword("SMILE"), 2,
            "查看卡组顶5张，公开其中最多2张《SMILE》卡牌加入手牌", trashRemainder: false);
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool Eligible(CardInstance card) => card.Info.Kind == CardKind.Character && card.Info.HasKeyword("SMILE")
            && ctx.State.CurrentPowerOf(ctx.OwnerIndex, card) <= 5000;
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnHandCharacter", "登场手牌中最多2张力量不高于5000的《SMILE》角色", me.Hand.Where(Eligible), 0, 2);
        foreach (var card in picked)
            if (!ctx.State.IsGameOver && me.Hand.Contains(card) && Eligible(card))
                await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
    }
}

/// <summary>你需要我：主要只可放回活跃咚；反击无此成本，单独给领袖加2000。</summary>
public sealed class EB05_040_YouNeedMe : IScriptedEffect
{
    public string CardNumber => "EB05-040";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.EventMain or EffectTrigger.EventCounter;
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.EventCounter) { AtomicOps.AddLeaderPowerThisBattle(ctx.State, ctx.OwnerIndex, 2000); return; }
        if (ctx.Trigger != EffectTrigger.EventMain) return;
        var dons = me.CostArea.Where(don => don.State == DonState.Active).ToList();
        if (dons.Count == 0) return;
        var answer = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReturnOwnDon", "可以将1张活跃咚放回咚!!卡组，或取消发动",
            dons.Select(don => don.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?> { ["donChoices"] = AtomicOps.BuildDonPromptChoices(me, dons), ["canCancel"] = true });
        if (answer.Count != 1) return;
        var don = dons.FirstOrDefault(candidate => candidate.Id.ToString() == answer[0]);
        if (don is null || !me.CostArea.Contains(don) || don.State != DonState.Active) return;
        don.State = DonState.InDeck;
        me.CostArea.Remove(don);
        me.DonDeck.Add(don);
        EffectRuntime.NotifyWatcher(EffectTrigger.OnDonReturnedToDeck,
            new Dictionary<string, object?> { ["count"] = 1, ["owner"] = ctx.OwnerIndex });
        if (!me.Leader.Info.HasKeyword("堂吉诃德海盗团")) return;
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 3);
        if (!ctx.State.IsGameOver) await October9CardHelpers.Discard(ctx, ctx.OwnerIndex, 1, isCost: false);
    }
}

/// <summary>特大幽灵：实际KO恐怖之船角色作为成本，KO触发延后至事件完整结算后。</summary>
public sealed class EB05_049_ExtraLargeGhost : IScriptedEffect
{
    public string CardNumber => "EB05-049";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.EventMain or EffectTrigger.EventCounter;
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        var picked = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OwnCharacter", "可以KO我方1张《恐怖之船海盗团》角色作为成本，或取消发动",
            me.Characters.Where(card => card.Info.HasKeyword("恐怖之船海盗团")));
        if (picked.Count != 1 || !me.Characters.Contains(picked[0]) || !picked[0].Info.HasKeyword("恐怖之船海盗团")) return;
        // 若KO被保护或置换，成本没有按卡面支付，不能结算冒号后的效果（综合规则8-3-1-7）。
        if (!await AtomicOps.KOByEffectAsync(ctx.State, ctx.OwnerIndex, picked[0], ctx.Prompts, ctx.OwnerIndex, deferOnKO: true)) return;
        if (ctx.Trigger == EffectTrigger.EventCounter) { await October9CardHelpers.BuffBattle(ctx); return; }
        var opponent = ctx.State.Players[1 - ctx.OwnerIndex];
        var targets = await October9CardHelpers.Pick(ctx, ctx.OwnerIndex, "OppCharacter", "本回合使对方最多1张角色费用-3", opponent.Characters);
        if (targets.Count == 1 && opponent.Characters.Contains(targets[0]))
            AtomicOps.AddCostModifier(targets[0], -3, KeywordDuration.ThisTurn);
    }
}

/// <summary>布玲：抽1并建立动态战斗KO保护，持续到下个对方结束阶段；触发抽1再登场4000力量触发角色。</summary>
public sealed class EB05_059_Pudding : IScriptedEffect
{
    public string CardNumber => "EB05-059";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.EventMain or EffectTrigger.OnLifeRevealTrigger;
    public async Task Resolve(EffectContext ctx)
    {
        if (!HandlesTrigger(ctx.Trigger)) return;
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        if (ctx.State.IsGameOver) return;
        int owner = ctx.OwnerIndex;
        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            ctx.State.ContinuousEffects.Add(new ContinuousEffect { SourceCardId = ctx.Source.Id.ToString(), SourceCardNumber = CardNumber,
                PersistsAfterSourceLeaves = true, ExpiresAtEndOfTurnForSide = 1 - owner,
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false }, KoGuard = "battle",
                Predicate = (state, side, card) => side == owner && card.Info.Kind == CardKind.Character
                    && card.Info.HasKeyword("大妈海盗团") && state.OriginalPowerOf(owner, card) == 4000 });
            return;
        }
        var me = ctx.State.Players[owner];
        bool Eligible(CardInstance card) => card.Info.Kind == CardKind.Character && ctx.State.CurrentPowerOf(owner, card) == 4000
            && !string.IsNullOrEmpty(card.Info.Trigger);
        var picked = await October9CardHelpers.Pick(ctx, owner, "OwnHandCharacter", "登场手牌中最多1张力量为4000且拥有【触发】的角色", me.Hand.Where(Eligible));
        if (picked.Count == 1 && me.Hand.Contains(picked[0]) && Eligible(picked[0]))
            await AtomicOps.PlayFromHandFree(ctx.State, owner, picked[0]);
    }
}
