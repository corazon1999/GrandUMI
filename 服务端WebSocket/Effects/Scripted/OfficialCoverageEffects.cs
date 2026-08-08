using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>本轮官网补卡共用的选择、成本与检索操作。</summary>
internal static class OfficialCoverageHelpers
{
    public static bool IsAttacking(EffectContext ctx)
        => ctx.State.CurrentBattle?.AttackerCardId == ctx.Source.Id;

    public static async Task<CardInstance?> ChooseUpToOne(
        EffectContext ctx,
        int chooser,
        string kind,
        string text,
        IReadOnlyList<CardInstance> cards)
    {
        if (cards.Count == 0) return null;
        var chosen = await ctx.Prompts.ChooseCards(chooser, kind, text,
            cards.Select(card => card.Id.ToString()).ToList(), 0, 1, ChoiceCards(cards));
        return chosen.Count == 0
            ? null
            : cards.FirstOrDefault(card => card.Id.ToString() == chosen[0]);
    }

    public static async Task<CardInstance?> ChooseRequiredOne(
        EffectContext ctx,
        int chooser,
        string kind,
        string text,
        IReadOnlyList<CardInstance> cards)
    {
        if (cards.Count == 0) return null;
        var chosen = await ctx.Prompts.ChooseCards(chooser, kind, text,
            cards.Select(card => card.Id.ToString()).ToList(), 1, 1, ChoiceCards(cards));
        return chosen.Count == 0
            ? null
            : cards.FirstOrDefault(card => card.Id.ToString() == chosen[0]);
    }

    public static async Task<List<CardInstance>> ChooseAnyNumber(
        EffectContext ctx,
        string kind,
        string text,
        IReadOnlyList<CardInstance> cards)
    {
        if (cards.Count == 0) return new List<CardInstance>();
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, kind, text,
            cards.Select(card => card.Id.ToString()).ToList(), 0, cards.Count, ChoiceCards(cards));
        var ids = chosen.ToHashSet(StringComparer.Ordinal);
        return cards.Where(card => ids.Contains(card.Id.ToString())).ToList();
    }

    public static async Task<bool> PayRestDon(EffectContext ctx, int amount)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var active = me.CostArea.Where(don => don.State == DonState.Active).ToList();
        if (active.Count < amount) return false;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnDonCost",
            $"选择要转为休息状态的 {amount} 张咚!!",
            active.Select(don => don.Id.ToString()).ToList(), 0, amount,
            new Dictionary<string, object?>
            {
                ["donChoices"] = active.Select(don => new { id = don.Id.ToString(), state = "Active" }).ToList(),
            });
        if (chosen.Count != amount) return false;
        foreach (var id in chosen)
        {
            var don = active.FirstOrDefault(item => item.Id.ToString() == id);
            if (don is not null) don.State = DonState.Rest;
        }
        return true;
    }

    public static async Task<CardInstance?> PayDiscardFromHand(EffectContext ctx, Func<CardInstance, bool>? filter = null)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var candidates = me.Hand.Where(card => filter?.Invoke(card) ?? true).ToList();
        var card = await ChooseUpToOne(ctx, ctx.OwnerIndex, "OwnHandDiscardCost", "选择要丢弃的1张手牌", candidates);
        if (card is null) return null;
        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        try { AtomicOps.DiscardHand(me, card); }
        finally { EffectRuntime.PayingCost = previous; }
        return card;
    }

    public static async Task<bool> PayTrashToDeckBottom(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var card = await ChooseUpToOne(ctx, ctx.OwnerIndex, "OwnTrashCost",
            "选择放回卡组最下方的1张废弃区卡牌", me.Trash);
        if (card is null) return false;
        AtomicOps.ReturnTrashToDeckBottom(me, card);
        return true;
    }

    public static void LifeTopToHand(GameState state, int owner)
    {
        var player = state.Players[owner];
        if (player.LifeArea.Count == 0) return;
        var card = player.LifeArea[0];
        player.LifeArea.RemoveAt(0);
        card.IsLifeFaceUp = false;
        player.Hand.Add(card);
    }

    public static void TrashLifeTop(GameState state, int owner)
    {
        var player = state.Players[owner];
        if (player.LifeArea.Count == 0) return;
        var card = player.LifeArea[0];
        player.LifeArea.RemoveAt(0);
        card.IsLifeFaceUp = false;
        player.Trash.Add(card);
    }

    public static async Task<CardInstance?> LookTopPickAndBottom(
        EffectContext ctx,
        int count,
        Func<CardInstance, bool> filter,
        string text)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var looked = me.Deck.Take(count).ToList();
        if (looked.Count == 0) return null;
        var picked = await ChooseUpToOne(ctx, ctx.OwnerIndex, "LookTopReveal", text, looked.Where(filter).ToList());
        if (picked is not null)
        {
            me.Deck.Remove(picked);
            me.Hand.Add(picked);
            ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new[] { picked.Info.Number });
        }

        var rest = looked.Where(me.Deck.Contains).ToList();
        if (rest.Count > 1)
        {
            var order = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReorderDeckBottom",
                "按选择顺序将剩余卡牌放回卡组最下方",
                rest.Select(card => card.Id.ToString()).ToList(), rest.Count, rest.Count, ChoiceCards(rest));
            if (order.Count == rest.Count)
                rest = order.Select(id => rest.First(card => card.Id.ToString() == id)).ToList();
        }
        foreach (var card in rest)
        {
            me.Deck.Remove(card);
            me.Deck.Add(card);
        }
        return picked;
    }

    public static Dictionary<string, object?> ChoiceCards(IEnumerable<CardInstance> cards)
        => new()
        {
            ["choiceCards"] = cards.Select(card => new
            {
                id = card.Id.ToString(),
                number = card.Info.Number,
            }).ToList(),
        };

    public static List<CardInstance> OwnLeaderAndCharacters(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var result = new List<CardInstance> { me.Leader };
        result.AddRange(me.Characters);
        return result;
    }
}

/// <summary>OP01-060 堂吉诃德·多弗拉门戈。</summary>
public sealed class OP01_060_Doflamingo : IScriptedEffect
{
    public string CardNumber => "OP01-060";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!OfficialCoverageHelpers.IsAttacking(ctx)
            || me.AttachedDonCount(ctx.Source.Id) < 2
            || !me.CostArea.Any(don => don.State == DonState.Active)) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "休息1张咚!!，公开卡组最上方1张卡牌并尝试登场？")) return;
        if (!await OfficialCoverageHelpers.PayRestDon(ctx, 1)) return;

        var top = me.Deck.FirstOrDefault();
        if (top is null) return;
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new[] { top.Info.Number });
        if (top.Info.Kind == CardKind.Character && top.Info.Cost <= 4 && top.Info.HasKeyword("王下七武海")
            && await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将公开的角色以休息状态登场？"))
            await AtomicOps.PlayFromDeckFree(ctx.State, ctx.OwnerIndex, top, restState: true);
    }
}

/// <summary>P-057 泡沫摇篮曲。</summary>
public sealed class P_057_BubbleLullaby : IScriptedEffect
{
    public string CardNumber => "P-057";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        if (!ctx.State.Players[ctx.OwnerIndex].Leader.Info.NameIs("乌塔")) return;
        int opponent = 1 - ctx.OwnerIndex;
        var candidates = ctx.State.Players[opponent].Characters
            .Where(card => card.IsTapped && ctx.State.CurrentCostOf(opponent, card) <= 4).ToList();
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择最多2张下个重置阶段不会转为活跃的对方角色",
            candidates.Select(card => card.Id.ToString()).ToList(), 0, 2,
            OfficialCoverageHelpers.ChoiceCards(candidates));
        foreach (var id in chosen)
        {
            var card = candidates.FirstOrDefault(item => item.Id.ToString() == id);
            if (card is not null) AtomicOps.PreventActivateNextReset(card);
        }
    }
}

/// <summary>P-058 风的去向。</summary>
public sealed class P_058_WheresTheWindGoing : IScriptedEffect
{
    public string CardNumber => "P-058";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.EventMain or EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            if (me.Leader.Info.NameIs("乌塔"))
                ctx.State.EndOfTurnTasks.Add(new EndTurnTask
                {
                    Kind = "RefreshAllFilmCharacters",
                    Owner = ctx.OwnerIndex,
                    SourceCardId = ctx.Source.Id.ToString(),
                });
        }
        else
        {
            foreach (var card in me.Characters.Where(card => card.Info.HasKeyword("FILM")))
                AtomicOps.ActivateCard(card);
        }
        return Task.CompletedTask;
    }
}

/// <summary>P-059 世界的延续。</summary>
public sealed class P_059_TheWorldsContinuation : IScriptedEffect
{
    public string CardNumber => "P-059";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Leader.Info.NameIs("乌塔")) return;
        var returned = await OfficialCoverageHelpers.ChooseAnyNumber(ctx, "OwnCharacter",
            "选择任意张要放回手牌的我方角色", me.Characters.ToList());
        foreach (var card in returned) AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, card);
        for (int i = 0; i < returned.Count; i++)
        {
            var target = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
                "OwnLeaderOrCharacter", "选择最多1张本次战斗力量+2000的我方领袖或角色",
                OfficialCoverageHelpers.OwnLeaderAndCharacters(ctx));
            if (target is not null) AtomicOps.AddPowerThisBattle(target, 2000);
        }
    }
}

/// <summary>P-060 消逝之歌。</summary>
public sealed class P_060_FleetingLullaby : IScriptedEffect
{
    public string CardNumber => "P-060";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var uta = new List<CardInstance>();
        if (!me.Leader.IsTapped && me.Leader.Info.NameIs("乌塔")) uta.Add(me.Leader);
        uta.AddRange(me.Characters.Where(card => !card.IsTapped && card.Info.NameIs("乌塔")));
        if (me.StageCard is { IsTapped: false } stage && stage.Info.NameIs("乌塔")) uta.Add(stage);
        var cost = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex, "OwnCardCost",
            "选择要转为休息状态的1张“乌塔”", uta);
        if (cost is null) return;
        AtomicOps.RestCard(cost);

        var opponent = ctx.State.Players[1 - ctx.OwnerIndex];
        var active = opponent.CostArea.Where(don => don.State == DonState.Active).ToList();
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentDon",
            "选择最多2张对方咚!!转为休息状态",
            active.Select(don => don.Id.ToString()).ToList(), 0, 2,
            new Dictionary<string, object?>
            {
                ["donChoices"] = active.Select(don => new { id = don.Id.ToString(), state = "Active" }).ToList(),
            });
        foreach (var id in chosen)
        {
            var don = active.FirstOrDefault(item => item.Id.ToString() == id);
            if (don is not null) don.State = DonState.Rest;
        }
    }
}

/// <summary>P-120 的效果由 HandStaticCost 统一计算；此标记使实现审计能定位到该卡。</summary>
public sealed class P_120_SanjiStaticCostMarker : IScriptedEffect
{
    public string CardNumber => "P-120";
    public bool HandlesTrigger(EffectTrigger trigger) => false;
    public Task Resolve(EffectContext ctx) => Task.CompletedTask;
}

/// <summary>P-121 布鲁克。</summary>
public sealed class P_121_Brook : IScriptedEffect
{
    public string CardNumber => "P-121";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnKO;

    public Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            AtomicOps.MillTop(ctx.State.Players[ctx.OwnerIndex], 3);
            return Task.CompletedTask;
        }
        return AtomicOps.OpponentDiscardChosen(ctx.State, ctx.Prompts, 1 - ctx.OwnerIndex, 2);
    }
}

/// <summary>P-122 艾斯&amp;萨波&amp;路飞。</summary>
public sealed class P_122_AceSaboLuffy : IScriptedEffect
{
    private static readonly string[] Names = ["萨波", "波特夹斯·D·艾斯", "蒙奇·D·路飞"];
    public string CardNumber => "P-122";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        string key = $"P-122-act:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);
        foreach (var name in Names)
        {
            var target = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
                "OwnLeaderOrCharacter", $"选择最多1张“{name}”本回合力量+1000",
                OfficialCoverageHelpers.OwnLeaderAndCharacters(ctx).Where(card => card.MatchesName(name)).ToList());
            if (target is not null) AtomicOps.AddPowerThisTurn(target, 1000);
        }
    }
}

/// <summary>P-126 萨波。</summary>
public sealed class P_126_Sabo : IScriptedEffect
{
    public string CardNumber => "P-126";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        if (!await OfficialCoverageHelpers.PayRestDon(ctx, 1)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        AtomicOps.GiveKeyword(ctx.Source, "速攻", KeywordDuration.ThisTurn, ctx.OwnerIndex);
        foreach (var name in new[] { "波特夹斯·D·艾斯", "蒙奇·D·路飞" })
        {
            var target = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
                "OwnCharacter", $"选择最多1张原本力量6000的“{name}”获得速攻",
                me.Characters.Where(card => card.Info.Power == 6000 && card.MatchesName(name)).ToList());
            if (target is not null)
                AtomicOps.GiveKeyword(target, "速攻", KeywordDuration.ThisTurn, ctx.OwnerIndex);
        }
    }
}

/// <summary>P-128 波特夹斯·D·艾斯。</summary>
public sealed class P_128_Ace : IScriptedEffect
{
    public string CardNumber => "P-128";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Characters.Any(card => card.MatchesName("萨波") || card.MatchesName("蒙奇·D·路飞"))) return;
        var target = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
            "OpponentCharacter", "选择对方最多1张角色，本回合力量-3000",
            ctx.State.Players[1 - ctx.OwnerIndex].Characters);
        if (target is not null) AtomicOps.AddPowerThisTurn(target, -3000);
    }
}

/// <summary>P-129 卷乃。</summary>
public sealed class P_129_Makino : IScriptedEffect
{
    public string CardNumber => "P-129";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var target = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
            "OwnCharacter", "选择我方最多1张原本力量6000的角色，本回合力量+2000",
            ctx.State.Players[ctx.OwnerIndex].Characters.Where(card => card.Info.Power == 6000).ToList());
        if (target is not null) AtomicOps.AddPowerThisTurn(target, 2000);
    }
}

/// <summary>P-130 蒙奇·D·戈普。</summary>
public sealed class P_130_Garp : IScriptedEffect
{
    public string CardNumber => "P-130";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        await OfficialCoverageHelpers.LookTopPickAndBottom(ctx, 5,
            card => card.Info.HasKeyword("海军"), "公开最多1张《海军》卡牌并加入手牌");
        var me = ctx.State.Players[ctx.OwnerIndex];
        var discard = await OfficialCoverageHelpers.ChooseRequiredOne(ctx, ctx.OwnerIndex,
            "OwnHandDiscard", "丢弃1张手牌", me.Hand);
        if (discard is not null) AtomicOps.DiscardHand(me, discard);
    }
}

/// <summary>P-132 蒙奇·D·路飞。</summary>
public sealed class P_132_Luffy : IScriptedEffect
{
    public string CardNumber => "P-132";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        if (!await OfficialCoverageHelpers.PayRestDon(ctx, 4)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        foreach (var name in new[] { "萨波", "波特夹斯·D·艾斯" })
        {
            var card = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
                "OwnHand", $"选择最多1张力量6000的“{name}”登场",
                me.Hand.Where(item => item.Info.Kind == CardKind.Character
                    && item.Info.Power == 6000 && item.MatchesName(name)).ToList());
            if (card is not null) await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
        }
    }
}

/// <summary>P-133 大和。</summary>
public sealed class P_133_Yamato : IScriptedEffect
{
    public string CardNumber => "P-133";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        Guid selfId = ctx.Source.Id;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            Predicate = (state, side, card) => side == owner && card.Id == selfId
                && state.Players[owner].Characters.Any(other => other.Id != selfId
                    && other.Info.EffectTags.Length == 0
                    && other.Info.Abilities.Length == 0
                    && string.IsNullOrEmpty(other.Info.Trigger)),
        });
        return Task.CompletedTask;
    }
}

/// <summary>P-134 吃霸王餐的惯犯。</summary>
public sealed class P_134_DineAndDash : IScriptedEffect
{
    private static readonly string[] Names = ["萨波", "波特夹斯·D·艾斯", "蒙奇·D·路飞"];
    public string CardNumber => "P-134";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var picked = await OfficialCoverageHelpers.LookTopPickAndBottom(ctx, 3,
            card => Names.Any(card.MatchesName), "公开最多1张“萨波”“艾斯”或“路飞”并加入手牌");
        if (picked is null) AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }
}

/// <summary>P-155 特拉法尔加·罗。</summary>
public sealed class P_155_TrafalgarLaw : IScriptedEffect
{
    public string CardNumber => "P-155";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnAttackDeclare or EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            if (ctx.State.Players[1 - ctx.OwnerIndex].LifeArea.Count <= 3
                && ctx.State.Players[ctx.OwnerIndex].Trash.Contains(ctx.Source))
                await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
            return;
        }

        if (!OfficialCoverageHelpers.IsAttacking(ctx)) return;
        var discarded = await OfficialCoverageHelpers.PayDiscardFromHand(ctx,
            card => !string.IsNullOrEmpty(card.Info.Trigger));
        if (discarded is null) return;
        var target = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
            "OpponentCharacter", "选择对方最多1张角色，本回合力量-2000",
            ctx.State.Players[1 - ctx.OwnerIndex].Characters);
        if (target is not null) AtomicOps.AddPowerThisTurn(target, -2000);
    }
}

/// <summary>ST19-003 达斯琪。</summary>
public sealed class ST19_003_Tashigi : IScriptedEffect
{
    public string CardNumber => "ST19-003";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnEnterField or EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        int opponent = 1 - ctx.OwnerIndex;
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (!ctx.State.Players[ctx.OwnerIndex].Leader.Info.NameIs("斯摩格")) return;
            var target = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
                "OpponentCharacter", "选择对方最多1张角色，本回合费用-4",
                ctx.State.Players[opponent].Characters);
            if (target is not null) AtomicOps.AddCostModifier(target, -4, KeywordDuration.ThisTurn);
            return;
        }

        var me = ctx.State.Players[ctx.OwnerIndex];
        string key = $"ST19-003-act:{ctx.Source.Id}";
        if (ctx.Source.TurnPlayed != ctx.State.TurnCount || me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);
        var victim = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
            "OpponentCharacter", "选择对方最多1张费用为0的角色放置到废弃区",
            ctx.State.Players[opponent].Characters
                .Where(card => ctx.State.CurrentCostOf(opponent, card) == 0).ToList());
        if (victim is not null) AtomicOps.TrashFieldCard(ctx.State, opponent, victim);
    }
}

/// <summary>ST19-004 日奈。</summary>
public sealed class ST19_004_Hina : IScriptedEffect
{
    public string CardNumber => "ST19-004";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnEnterField or EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        var me = ctx.State.Players[owner];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            Guid selfId = ctx.Source.Id;
            ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == selfId.ToString());
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                CostDelta = 4,
                Predicate = (state, side, card) => side == owner && card.Id == selfId
                    && state.CurrentTurnPlayer != owner
                    && state.Players[owner].AttachedDonCount(selfId) >= 1,
            });
            return;
        }

        string key = $"ST19-004-act:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key) || !await OfficialCoverageHelpers.PayTrashToDeckBottom(ctx)) return;
        me.TurnOnceUsed.Add(key);
        var targets = OfficialCoverageHelpers.OwnLeaderAndCharacters(ctx)
            .Where(card => me.AttachedDonCount(card.Id) < 10).ToList();
        var target = await OfficialCoverageHelpers.ChooseRequiredOne(ctx, owner,
            "OwnLeaderOrCharacter", "选择1张我方领袖或角色，赋予最多1张休息咚!!", targets);
        if (target is not null) AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
    }
}

/// <summary>ST19-005 蒙奇·D·戈普。</summary>
public sealed class ST19_005_Garp : IScriptedEffect
{
    public string CardNumber => "ST19-005";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        string key = $"ST19-005-act:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key) || !await OfficialCoverageHelpers.PayTrashToDeckBottom(ctx)) return;
        me.TurnOnceUsed.Add(key);
        var target = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
            "OpponentCharacter", "选择对方最多1张角色，本回合费用-1",
            ctx.State.Players[1 - ctx.OwnerIndex].Characters);
        if (target is not null) AtomicOps.AddCostModifier(target, -1, KeywordDuration.ThisTurn);
    }
}

/// <summary>ST20-004 夏洛特·布玲。</summary>
public sealed class ST20_004_Pudding : IScriptedEffect
{
    public string CardNumber => "ST20-004";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            int opponent = 1 - ctx.OwnerIndex;
            var target = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
                "OpponentCharacter", "选择对方最多1张费用不高于3的活跃角色转为休息状态",
                ctx.State.Players[opponent].Characters.Where(card => !card.IsTapped
                    && ctx.State.CurrentCostOf(opponent, card) <= 3).ToList());
            if (target is not null) AtomicOps.RestCard(target);
            return;
        }

        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.LifeArea.Count == 0 || ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将我方生命区最上方1张卡牌加入手牌，转活最多1张低费《大妈海盗团》角色？")) return;
        OfficialCoverageHelpers.LifeTopToHand(ctx.State, ctx.OwnerIndex);
        var targetOwn = await OfficialCoverageHelpers.ChooseUpToOne(ctx, ctx.OwnerIndex,
            "OwnCharacter", "选择我方最多1张费用不高于3且休息中的《大妈海盗团》角色转为活跃状态",
            me.Characters.Where(card => card.IsTapped && card.Info.HasKeyword("大妈海盗团")
                && ctx.State.CurrentCostOf(ctx.OwnerIndex, card) <= 3).ToList());
        if (targetOwn is not null) AtomicOps.ActivateCard(targetOwn);
    }
}

/// <summary>ST20-005 夏洛特·玲玲。</summary>
public sealed class ST20_005_BigMom : IScriptedEffect
{
    public string CardNumber => "ST20-005";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        if (await OfficialCoverageHelpers.PayDiscardFromHand(ctx) is null) return;
        int opponent = 1 - ctx.OwnerIndex;
        int option = await ctx.Prompts.ChooseOption(opponent, "选择夏洛特·玲玲效果的处理项",
            new[] { "丢弃2张手牌", "将生命区最上方1张卡牌放置到废弃区" });
        if (option == 0)
            await AtomicOps.OpponentDiscardChosen(ctx.State, ctx.Prompts, opponent, 2);
        else
            OfficialCoverageHelpers.TrashLifeTop(ctx.State, opponent);
    }
}
