using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Validation;

namespace GrandUMI.Effects.Scripted;

/// <summary>OP18 与 EB05 首批新卡共用的选择及费用工具。</summary>
internal static class OP18EB05EffectHelpers
{
    public static async Task<List<CardInstance>> Pick(
        EffectContext ctx,
        string kind,
        string text,
        IEnumerable<CardInstance> source,
        int min,
        int max)
    {
        var cards = source.DistinctBy(card => card.Id).ToList();
        if (cards.Count == 0 || max <= 0) return [];
        max = Math.Min(max, cards.Count);
        min = Math.Min(min, max);
        var ids = await ctx.Prompts.ChooseCards(
            ctx.OwnerIndex,
            kind,
            text,
            cards.Select(card => card.Id.ToString()).ToList(),
            min,
            max,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = cards.Select(card => new
                {
                    id = card.Id.ToString(),
                    number = card.Info.Number,
                }).ToList(),
            });

        return ids
            .Select(id => cards.FirstOrDefault(card => card.Id.ToString() == id))
            .Where(card => card is not null)
            .Cast<CardInstance>()
            .DistinctBy(card => card.Id)
            .ToList();
    }

    public static async Task<CardInstance?> DiscardOne(
        EffectContext ctx,
        string text,
        bool isCost)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var picked = await Pick(ctx, "OwnHandDiscard", text, me.Hand, 1, 1);
        if (picked.Count == 0) return null;

        bool previous = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = isCost;
        try { AtomicOps.DiscardHand(me, picked[0]); }
        finally { EffectRuntime.PayingCost = previous; }
        return picked[0];
    }

    public static CardInstance? FindOwnedCard(PlayerState player, string? id)
    {
        if (id is null) return null;
        if (player.Leader.Id.ToString() == id) return player.Leader;
        var stage = player.StageCards.FirstOrDefault(card => card.Id.ToString() == id);
        if (stage is not null) return stage;
        return player.Characters
            .Concat(player.Hand)
            .Concat(player.Trash)
            .Concat(player.Deck)
            .Concat(player.LifeArea)
            .FirstOrDefault(card => card.Id.ToString() == id);
    }
}

/// <summary>
/// OP18-021 弗兰奇：舞台手牌获得反击+3000；咚!!-1 后登场手牌中费用不高于5的舞台。
/// 静态反击值由 HandStaticCounter 统一计算。
/// </summary>
public sealed class OP18_021_Franky : IScriptedEffect
{
    public string CardNumber => "OP18-021";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.ActivatedMain || ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        string key = $"OP18-021-main:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key)) return;

        var candidates = me.Hand.Where(card =>
            card.Info.Kind == CardKind.Stage && ctx.State.CurrentCostOf(card) <= 5).ToList();
        if (candidates.Count == 0 || me.TotalDonInCostArea == 0) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        var picked = await OP18EB05EffectHelpers.Pick(
            ctx, "OwnHandStage", "将手牌中最多1张费用不高于5的舞台卡牌登场", candidates, 0, 1);
        if (picked.Count > 0) await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked[0]);
        me.TurnOnceUsed.Add(key);
    }
}

/// <summary>
/// OP18-031 妮古·罗宾：休息自身替代我方角色因对方效果离场；我方回合结束时活跃
/// 最多1张《七水之城》卡牌与最多1张咚!!。
/// </summary>
public sealed class OP18_031_NicoRobin : IScriptedEffect
{
    public string CardNumber => "OP18-031";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is
        EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField or EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            await ResolveTurnEnd(ctx);
            return;
        }

        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;
        var victimId = ctx.Vars.TryGetValue("victimId", out var rawVictim) ? rawVictim as string : null;
        int victimOwner = ctx.Vars.TryGetValue("victimOwner", out var rawOwner) && rawOwner is int owner
            ? owner
            : -1;
        var victim = me.Characters.FirstOrDefault(card => card.Id.ToString() == victimId);
        if (victimOwner != ctx.OwnerIndex || victim is null) return;

        if (ctx.Trigger == EffectTrigger.OnAllyWillBeKOd
            && (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex)) return;
        if (ctx.Source.IsTapped
            || ctx.Source.HasRestriction(RestrictionKind.CannotBeRested)
            || ctx.State.HasContinuousRestriction(ctx.Source, RestrictionKind.CannotBeRested)) return;

        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            $"妮古·罗宾：将此角色转为休息状态，使「{victim.Info.Name}」不离场？")) return;

        if (!AtomicOps.RestCard(ctx.Source)) return;
        ctx.State.MarkPreventEffectLeaveBatch(
            ctx.OwnerIndex,
            victim.Id,
            _ => true,
            isKoReplacement: ctx.Trigger == EffectTrigger.OnAllyWillBeKOd);
    }

    private static async Task ResolveTurnEnd(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var ownCards = new[] { me.Leader }
            .Concat(me.Characters)
            .Concat(me.StageCards)
            .Where(card => card.IsTapped && card.Info.HasKeyword("七水之城"));
        var picked = await OP18EB05EffectHelpers.Pick(
            ctx, "OwnRestCard", "将我方最多1张拥有《七水之城》特征的卡牌转为活跃状态", ownCards, 0, 1);
        if (picked.Count > 0) AtomicOps.ActivateCard(picked[0]);

        var restDons = me.CostArea.Where(don => don.State == DonState.Rest).ToList();
        if (restDons.Count == 0) return;
        var donIds = await ctx.Prompts.ChooseCards(
            ctx.OwnerIndex,
            "OwnRestDon",
            "将我方最多1张咚!!转为活跃状态",
            restDons.Select(don => don.Id.ToString()).ToList(),
            0,
            1,
            new Dictionary<string, object?>
            {
                ["donChoices"] = restDons.Select(don => new
                {
                    id = don.Id.ToString(),
                    state = don.State.ToString(),
                }).ToList(),
            });
        var selected = restDons.FirstOrDefault(don => don.Id.ToString() == donIds.FirstOrDefault());
        if (selected is not null) selected.State = DonState.Active;
    }
}

/// <summary>
/// OP18-060 军子宫：我方角色从废弃区登场时每回合一次抽2弃1；弃1手牌后，
/// 若场上存在原本力量8000以上角色，则追加1张活跃咚!!。
/// </summary>
public sealed class OP18_060_Gunko : IScriptedEffect
{
    public string CardNumber => "OP18-060";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is
        EffectTrigger.OnAllyCharEnter or EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnAllyCharEnter)
        {
            int enteredOwner = ctx.Vars.TryGetValue("owner", out var rawOwner) && rawOwner is int owner
                ? owner
                : -1;
            var from = ctx.Vars.TryGetValue("from", out var rawFrom) ? rawFrom as string : null;
            string key = $"OP18-060-trash-enter:{ctx.Source.Id}";
            if (enteredOwner != ctx.OwnerIndex || from != "trash" || me.TurnOnceUsed.Contains(key)) return;

            me.TurnOnceUsed.Add(key);
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
            if (me.Hand.Count > 0)
                await OP18EB05EffectHelpers.DiscardOne(ctx, "抽取2张卡牌后，丢弃1张手牌", isCost: false);
            return;
        }

        if (ctx.Trigger != EffectTrigger.ActivatedMain || ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;
        string activeKey = $"OP18-060-main:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(activeKey) || me.Hand.Count == 0) return;
        if (!me.Characters.Any(card => ctx.State.OriginalPowerOf(ctx.OwnerIndex, card) >= 8000)) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "军子宫【每回合1次】：丢弃1张手牌，从咚!!卡组追加最多1张活跃咚!!？")) return;
        if (await OP18EB05EffectHelpers.DiscardOne(ctx, "选择丢弃1张手牌作为发动成本", isCost: true) is null) return;

        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        me.TurnOnceUsed.Add(activeKey);
    }
}

/// <summary>OP18-065 军子宫：阻挡者；登场时咚!!-1 后从废弃区登场力量6000以下《天龙人》角色。</summary>
public sealed class OP18_065_Gunko : IScriptedEffect
{
    public string CardNumber => "OP18-065";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger != EffectTrigger.OnEnterField) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        var candidates = me.Trash.Where(card =>
            card.Info.Kind == CardKind.Character
            && card.Info.Power <= 6000
            && card.Info.HasKeyword("天龙人")).ToList();
        if (candidates.Count == 0 || me.TotalDonInCostArea == 0) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        var picked = await OP18EB05EffectHelpers.Pick(
            ctx, "OwnTrashCharacter", "将废弃区中最多1张力量不高于6000的《天龙人》角色登场", candidates, 0, 1);
        if (picked.Count > 0) await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked[0]);
    }
}

/// <summary>OP18-078 迷你梅利2号：登场抽1并追加休息咚；休息舞台后给最多4张领袖/角色各赋予1咚。</summary>
public sealed class OP18_078_MiniMerryII : IScriptedEffect
{
    public string CardNumber => "OP18-078";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnEnterField or EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
            return;
        }

        if (ctx.Trigger != EffectTrigger.ActivatedMain
            || ctx.State.CurrentTurnPlayer != ctx.OwnerIndex
            || !me.StageCards.Contains(ctx.Source)
            || ctx.Source.IsTapped
            || ctx.Source.HasRestriction(RestrictionKind.CannotBeRested)
            || ctx.State.HasContinuousRestriction(ctx.Source, RestrictionKind.CannotBeRested)) return;

        var restedDons = me.CostArea.Where(don => don.State == DonState.Rest).ToList();
        if (restedDons.Count == 0) return;
        var targets = new[] { me.Leader }.Concat(me.Characters);
        var picked = await OP18EB05EffectHelpers.Pick(
            ctx,
            "OwnLeaderOrCharacter",
            "选择合计最多4张领袖或角色，分别赋予各1张咚!!",
            targets,
            0,
            Math.Min(4, restedDons.Count));

        if (!AtomicOps.RestCard(ctx.Source)) return;
        foreach (var target in picked)
        {
            var don = me.CostArea.FirstOrDefault(item => item.State == DonState.Rest);
            if (don is null) break;
            don.State = DonState.Attached;
            don.AttachedToCardId = target.Id;
        }
    }
}

/// <summary>OP18-119 夏姆洛克宫：KO时弃1手牌从废弃区重新登场；登场回合每回合一次复活费用6以下《神之骑士团》。</summary>
public sealed class OP18_119_Shamrock : IScriptedEffect
{
    public string CardNumber => "OP18-119";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnKO or EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            if (!me.Trash.Contains(ctx.Source) || me.Hand.Count == 0) return;
            if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "夏姆洛克宫【KO时】：丢弃1张手牌，从废弃区登场此角色？")) return;
            if (await OP18EB05EffectHelpers.DiscardOne(ctx, "选择丢弃1张手牌作为发动成本", isCost: true) is null) return;
            await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
            return;
        }

        string key = $"OP18-119-main:{ctx.Source.Id}";
        if (ctx.Trigger != EffectTrigger.ActivatedMain
            || ctx.State.CurrentTurnPlayer != ctx.OwnerIndex
            || ctx.Source.TurnPlayed != ctx.State.TurnCount
            || !me.Characters.Contains(ctx.Source)
            || me.TurnOnceUsed.Contains(key)) return;

        var candidates = me.Trash.Where(card =>
            card.Info.Kind == CardKind.Character
            && ctx.State.CurrentCostOf(card) <= 6
            && card.Info.HasKeyword("神之骑士团")).ToList();
        var picked = await OP18EB05EffectHelpers.Pick(
            ctx, "OwnTrashCharacter", "将废弃区中最多1张费用不高于6的《神之骑士团》角色登场", candidates, 0, 1);
        if (picked.Count > 0) await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked[0]);
        me.TurnOnceUsed.Add(key);
    }
}

/// <summary>EB05-010 妮古·罗宾：知属性非阻挡者被KO时每回合一次补1生命；主动活跃力量6000以下知属性角色。</summary>
public sealed class EB05_010_NicoRobin : IScriptedEffect
{
    public string CardNumber => "EB05-010";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnAnyCharKOd or EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnAnyCharKOd)
        {
            int koOwner = ctx.Vars.TryGetValue("owner", out var rawOwner) && rawOwner is int owner
                ? owner
                : -1;
            var cardId = ctx.Vars.TryGetValue("cardId", out var rawCard) ? rawCard as string : null;
            var knockedOut = OP18EB05EffectHelpers.FindOwnedCard(me, cardId);
            string key = $"EB05-010-ko:{ctx.Source.Id}";
            if (koOwner != ctx.OwnerIndex
                || knockedOut is null
                || !knockedOut.HasProperty("知")
                || ActionValidator.HasKeyword(ctx.State, knockedOut, "阻挡者")
                || me.TurnOnceUsed.Contains(key)
                || me.Deck.Count == 0) return;

            if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "妮古·罗宾【每回合1次】：将卡组最上方最多1张卡牌加入生命区最上方？")) return;
            AtomicOps.AddLifeFromDeckTop(me, 1);
            me.TurnOnceUsed.Add(key);
            return;
        }

        if (ctx.Trigger != EffectTrigger.ActivatedMain || ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;
        string activeKey = $"EB05-010-main:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(activeKey)) return;
        var candidates = me.Characters.Where(card =>
            card.IsTapped
            && ctx.State.CurrentPowerOf(ctx.OwnerIndex, card) <= 6000
            && card.HasProperty("知"));
        var picked = await OP18EB05EffectHelpers.Pick(
            ctx, "OwnCharacter", "将我方最多1张力量不高于6000且拥有属性（知）的角色转为活跃状态", candidates, 0, 1);
        if (picked.Count > 0) AtomicOps.ActivateCard(picked[0]);
        me.TurnOnceUsed.Add(activeKey);
    }
}

/// <summary>EB05-016 妮古·罗宾：我方回合登场时免费登场费用5以下知属性角色；触发休息对方费用6以下角色。</summary>
public sealed class EB05_016_NicoRobin : IScriptedEffect
{
    public string CardNumber => "EB05-016";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;
            var candidates = ctx.State.Players[ctx.OwnerIndex].Hand.Where(card =>
                card.Info.Kind == CardKind.Character
                && ctx.State.CurrentCostOf(card) <= 5
                && card.HasProperty("知"));
            var picked = await OP18EB05EffectHelpers.Pick(
                ctx, "OwnHandCharacter", "将手牌中最多1张费用不高于5且拥有属性（知）的角色登场", candidates, 0, 1);
            if (picked.Count > 0) await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked[0]);
            return;
        }

        if (ctx.Trigger != EffectTrigger.OnLifeRevealTrigger) return;
        var targets = ctx.State.Players[1 - ctx.OwnerIndex].Characters.Where(card =>
            !card.IsTapped && ctx.State.CurrentCostOf(card) <= 6);
        var triggerPick = await OP18EB05EffectHelpers.Pick(
            ctx, "OpponentCharacter", "将对方最多1张费用不高于6的角色转为休息状态", targets, 0, 1);
        if (triggerPick.Count > 0) AtomicOps.RestCard(triggerPick[0]);
    }
}
