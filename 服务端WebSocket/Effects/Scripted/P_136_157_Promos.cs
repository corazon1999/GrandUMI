using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>P-136～P-151、P-157 宣传卡共用的精确选择与条件判断。</summary>
internal static class P136P157Helpers
{
    public static List<CardInstance> OwnLeaderAndCharacters(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var cards = new List<CardInstance> { me.Leader };
        cards.AddRange(me.Characters);
        return cards;
    }

    public static List<CardInstance> OpponentLeaderAndCharacters(EffectContext ctx)
    {
        var opponent = ctx.State.Players[1 - ctx.OwnerIndex];
        var cards = new List<CardInstance> { opponent.Leader };
        cards.AddRange(opponent.Characters);
        return cards;
    }

    public static bool IsOwnLeaderOrCharacter(EffectContext ctx, CardInstance card)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        return ReferenceEquals(me.Leader, card) || me.Characters.Contains(card);
    }

    public static bool HasExtremeCostCharacter(GameState state)
    {
        for (int side = 0; side < state.Players.Length; side++)
            if (state.Players[side].Characters.Any(card =>
                    state.CurrentCostOf(side, card) == 0 || state.CurrentCostOf(side, card) >= 8))
                return true;
        return false;
    }

    public static bool HasZeroCostCharacter(GameState state)
    {
        for (int side = 0; side < state.Players.Length; side++)
            if (state.Players[side].Characters.Any(card => state.CurrentCostOf(side, card) == 0))
                return true;
        return false;
    }

    public static async Task<CardInstance?> ChooseUpToOne(
        EffectContext ctx, int chooser, string kind, string text, IReadOnlyList<CardInstance> cards)
        => await OfficialCoverageHelpers.ChooseUpToOne(ctx, chooser, kind, text, cards);

    public static async Task AttachOneRestDonToChosenTarget(
        EffectContext ctx, Func<CardInstance, bool>? targetFilter = null)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.CostArea.Any(don => don.State == DonState.Rest)) return;
        var targets = OwnLeaderAndCharacters(ctx)
            .Where(card => targetFilter?.Invoke(card) ?? true)
            .ToList();
        var target = await ChooseUpToOne(
            ctx, ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择我方最多1张领袖或角色，赋予1张休息状态的咚!!", targets);
        if (target is null || !IsOwnLeaderOrCharacter(ctx, target)) return;
        AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
    }
}

/// <summary>P-136 撒谎布：休息自身，给我方《草帽一伙》领袖或角色赋予至多1张休息咚。</summary>
public sealed class P_136_Usopp : IScriptedEffect
{
    public string CardNumber => "P-136";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Source.IsTapped || !AtomicOps.CanRestCard(ctx.State, ctx.Source)) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将撒谎布转为休息状态，发动效果？")) return;
        if (!me.Characters.Contains(ctx.Source) || !AtomicOps.RestCard(ctx.State, ctx.Source)) return;
        await P136P157Helpers.AttachOneRestDonToChosenTarget(
            ctx, card => card.Info.HasKeyword("草帽一伙"));
    }
}

/// <summary>P-137 山智：攻击时给我方领袖或角色赋予至多1张休息咚。</summary>
public sealed class P_137_Sanji : IScriptedEffect
{
    public string CardNumber => "P-137";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnAttackDeclare;
    public Task Resolve(EffectContext ctx)
        => P136P157Helpers.AttachOneRestDonToChosenTarget(ctx);
}

/// <summary>P-138 托尼托尼·乔巴：对方回合中自身力量+2000。</summary>
public sealed class P_138_TonyTonyChopper : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "P-138";
    public bool HandlesTrigger(EffectTrigger trigger) => false;

    public Task RegisterFieldStatic(EffectContext ctx)
    {
        var sourceId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == sourceId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = sourceId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            Predicate = (state, side, card) =>
                side == owner && card.Id == sourceId && state.CurrentTurnPlayer != owner,
        });
        return Task.CompletedTask;
    }

    public Task Resolve(EffectContext ctx) => Task.CompletedTask;
}

/// <summary>P-139 奈美：登场时赋予休息咚；咚×1攻击时抽1。</summary>
public sealed class P_139_Nami : IScriptedEffect
{
    public string CardNumber => "P-139";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            await P136P157Helpers.AttachOneRestDonToChosenTarget(ctx);
            return;
        }
        if (ctx.State.Players[ctx.OwnerIndex].AttachedDonCount(ctx.Source.Id) >= 1)
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
    }
}

/// <summary>P-140 蒙奇·D·路飞：登场时给我方一个领袖或角色赋予至多2张休息咚。</summary>
public sealed class P_140_MonkeyDLuffy : IScriptedEffect
{
    public string CardNumber => "P-140";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.CostArea.Any(don => don.State == DonState.Rest)) return;
        var target = await P136P157Helpers.ChooseUpToOne(
            ctx, ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择我方最多1张领袖或角色，赋予最多2张休息状态的咚!!",
            P136P157Helpers.OwnLeaderAndCharacters(ctx));
        if (target is null) return;
        await AtomicOps.PromptChooseAndApplyDonCount(
            ctx.State, ctx.Prompts, ctx.OwnerIndex, 2,
            "选择要赋予的休息状态咚!!数量",
            don => P136P157Helpers.IsOwnLeaderOrCharacter(ctx, target) && don.State == DonState.Rest,
            don =>
            {
                don.State = DonState.Attached;
                don.AttachedToCardId = target.Id;
            });
    }
}

/// <summary>P-141 罗罗诺亚·佐罗：登场时令对方一个领袖或角色本回合力量-1000。</summary>
public sealed class P_141_RoronoaZoro : IScriptedEffect
{
    public string CardNumber => "P-141";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        int opponent = 1 - ctx.OwnerIndex;
        var target = await P136P157Helpers.ChooseUpToOne(
            ctx, ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "选择对方最多1张领袖或角色，本回合力量-1000",
            P136P157Helpers.OpponentLeaderAndCharacters(ctx));
        if (target is null) return;
        var opponentState = ctx.State.Players[opponent];
        if (!ReferenceEquals(opponentState.Leader, target) && !opponentState.Characters.Contains(target)) return;
        AtomicOps.AddPowerThisTurn(target, -1000);
    }
}

/// <summary>P-142 前进·梅利号：将本舞台送废弃，代替符合条件的我方角色被KO。</summary>
public sealed class P_142_GoingMerry : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "P-142";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd;

    public bool IsTriggerAvailable(
        GameState state,
        int ownerIndex,
        CardInstance source,
        EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
    {
        if (trigger != EffectTrigger.OnAllyWillBeKOd) return false;
        var me = state.Players[ownerIndex];
        if (!me.StageCards.Any(stage => stage.Id == source.Id)) return false;
        if (payload is null
            || !payload.TryGetValue("victimOwner", out var rawOwner)
            || rawOwner is not int victimOwner
            || victimOwner != ownerIndex
            || !payload.TryGetValue("victimId", out var rawId)
            || rawId is not string victimId) return false;
        var victim = me.Characters.FirstOrDefault(card => card.Id.ToString() == victimId);
        return victim is not null
            && victim.Info.Power <= 8000
            && victim.Info.HasKeyword("草帽一伙");
    }

    public async Task Resolve(EffectContext ctx)
    {
        if (!IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        string victimId = (string)ctx.Vars["victimId"]!;
        var victim = me.Characters.First(card => card.Id.ToString() == victimId);
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                $"将「前进·梅利号」放置到废弃区，使「{victim.Info.Name}」不会被KO？")) return;

        // Prompt 可能跨越断线重连；支付前必须以恢复后的权威状态完整重验。
        if (!IsTriggerAvailable(ctx.State, ctx.OwnerIndex, ctx.Source, ctx.Trigger, ctx.Vars)) return;
        victim = me.Characters.First(card => card.Id.ToString() == victimId);
        AtomicOps.TrashFieldCard(ctx.State, ctx.OwnerIndex, ctx.Source, ignoreEffectLeaveGuard: true);
        if (me.StageCards.Any(stage => stage.Id == ctx.Source.Id) || !me.Trash.Contains(ctx.Source)) return;

        // 成本支付与整批防KO标记之间没有 await，不会暴露“舞台已离场但代替尚未生效”的中间快照。
        ctx.State.MarkPreventEffectLeaveBatch(
            ctx.OwnerIndex,
            victim.Id,
            card => card.Info.Power <= 8000 && card.Info.HasKeyword("草帽一伙"),
            isKoReplacement: true);
    }
}

/// <summary>P-143 克洛克达尔：登场时场上有当前费用0角色则本回合获得速攻。</summary>
public sealed class P_143_Crocodile : IScriptedEffect
{
    public string CardNumber => "P-143";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;
    public Task Resolve(EffectContext ctx)
    {
        if (P136P157Helpers.HasZeroCostCharacter(ctx.State))
            AtomicOps.GiveKeyword(ctx.Source, "速攻", KeywordDuration.ThisTurn, ctx.OwnerIndex);
        return Task.CompletedTask;
    }
}

/// <summary>P-144 Miss.全周日：KO我方一张巴洛克工作室角色作为成本，抽1。</summary>
public sealed class P_144_MissAllSunday : IScriptedEffect
{
    public string CardNumber => "P-144";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var candidates = me.Characters
            .Where(card => card.Info.HasKeywordContaining("巴洛克工作室"))
            .ToList();
        var cost = await P136P157Helpers.ChooseUpToOne(
            ctx, ctx.OwnerIndex, "OwnCharacterKOCost",
            "选择KO我方1张《巴洛克工作室》角色作为成本（不选择则不发动）", candidates);
        if (cost is null
            || !me.Characters.Contains(cost)
            || !cost.Info.HasKeywordContaining("巴洛克工作室")) return;
        bool wasKOd = await AtomicOps.KOByEffectAsync(
            ctx.State, ctx.OwnerIndex, cost, ctx.Prompts, ctx.OwnerIndex, deferOnKO: true);
        if (wasKOd) await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
    }
}

/// <summary>P-145 Miss.星期三：登场抽1弃1；KO时对方手牌至少6张则由对方弃2。</summary>
public sealed class P_145_MissWednesday : IScriptedEffect
{
    public string CardNumber => "P-145";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            int opponent = 1 - ctx.OwnerIndex;
            if (ctx.State.Players[opponent].Hand.Count >= 6)
                await AtomicOps.OpponentDiscardChosen(ctx.State, ctx.Prompts, opponent, 2);
            return;
        }

        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        var me = ctx.State.Players[ctx.OwnerIndex];
        var discard = await OfficialCoverageHelpers.ChooseRequiredOne(
            ctx, ctx.OwnerIndex, "OwnHand", "选择丢弃1张手牌", me.Hand.ToList());
        if (discard is not null && me.Hand.Contains(discard)) AtomicOps.DiscardHand(me, discard);
    }
}

/// <summary>P-146 Miss.黄金周：KO时抽1，并休息对方至多一张当前费用0角色。</summary>
public sealed class P_146_MissGoldenWeek : IScriptedEffect
{
    public string CardNumber => "P-146";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        int opponent = 1 - ctx.OwnerIndex;
        var opponentState = ctx.State.Players[opponent];
        var candidates = opponentState.Characters
            .Where(card => ctx.State.CurrentCostOf(opponent, card) == 0).ToList();
        var target = await P136P157Helpers.ChooseUpToOne(
            ctx, ctx.OwnerIndex, "OpponentCharacter", "选择对方最多1张费用为0的角色转为休息状态", candidates);
        if (target is not null
            && opponentState.Characters.Contains(target)
            && ctx.State.CurrentCostOf(opponent, target) == 0)
            AtomicOps.RestCard(ctx.State, target);
    }
}

/// <summary>P-147 Miss.情人节：极端费用角色存在时自身+2000；KO时回收至多一张巴洛克角色。</summary>
public sealed class P_147_MissValentine : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "P-147";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnKO;

    public Task RegisterFieldStatic(EffectContext ctx)
    {
        var sourceId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == sourceId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = sourceId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            Predicate = (state, side, card) =>
                side == owner && card.Id == sourceId && P136P157Helpers.HasExtremeCostCharacter(state),
        });
        return Task.CompletedTask;
    }

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var candidates = me.Trash.Where(card =>
            card.Info.Kind == CardKind.Character
            && card.Info.HasKeywordContaining("巴洛克工作室")).ToList();
        var target = await P136P157Helpers.ChooseUpToOne(
            ctx, ctx.OwnerIndex, "OwnTrashCharacter",
            "将废弃区中最多1张《巴洛克工作室》角色卡加入手牌", candidates);
        if (target is not null && me.Trash.Contains(target)) AtomicOps.TrashToHand(me, target);
    }
}

/// <summary>P-148 Mr.3：每回合1次，场上有当前费用0或8以上角色时赋予至多一张休息咚。</summary>
public sealed class P_148_Mr3 : IScriptedEffect
{
    public string CardNumber => "P-148";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        string key = $"P-148-act:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key) || !P136P157Helpers.HasExtremeCostCharacter(ctx.State)) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "发动Mr.3的【每回合1次】效果？")) return;
        if (!me.Characters.Contains(ctx.Source)
            || me.TurnOnceUsed.Contains(key)
            || !P136P157Helpers.HasExtremeCostCharacter(ctx.State)) return;
        me.TurnOnceUsed.Add(key);
        await P136P157Helpers.AttachOneRestDonToChosenTarget(ctx);
    }
}

/// <summary>P-149 Mr.5：登场时场上有当前费用0或8以上角色则抽2弃1。</summary>
public sealed class P_149_Mr5 : IScriptedEffect
{
    public string CardNumber => "P-149";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        if (!P136P157Helpers.HasExtremeCostCharacter(ctx.State)) return;
        await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 2);
        var me = ctx.State.Players[ctx.OwnerIndex];
        var discard = await OfficialCoverageHelpers.ChooseRequiredOne(
            ctx, ctx.OwnerIndex, "OwnHand", "选择丢弃1张手牌", me.Hand.ToList());
        if (discard is not null && me.Hand.Contains(discard)) AtomicOps.DiscardHand(me, discard);
    }
}

/// <summary>P-150 库赞：我方回合登场时复活费用1触发角色；生命触发抽1并禁攻。</summary>
public sealed class P_150_Kuzan : IScriptedEffect, ITriggeredEffectAvailability
{
    public string CardNumber => "P-150";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnLifeRevealTrigger;

    public bool IsTriggerAvailable(
        GameState state,
        int ownerIndex,
        CardInstance source,
        EffectTrigger trigger,
        IReadOnlyDictionary<string, object?>? payload)
        => trigger != EffectTrigger.OnEnterField || state.CurrentTurnPlayer == ownerIndex;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
            int opponent = 1 - ctx.OwnerIndex;
            var opponentState = ctx.State.Players[opponent];
            var candidates = opponentState.Characters
                .Where(card => ctx.State.CurrentCostOf(opponent, card) <= 6).ToList();
            var target = await P136P157Helpers.ChooseUpToOne(
                ctx, ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多1张费用不高于6的角色，本回合中无法攻击", candidates);
            if (target is not null
                && opponentState.Characters.Contains(target)
                && ctx.State.CurrentCostOf(opponent, target) <= 6)
                AtomicOps.AddRestriction(target, RestrictionKind.CannotAttack, KeywordDuration.ThisTurn, ctx.OwnerIndex);
            return;
        }

        var me = ctx.State.Players[ctx.OwnerIndex];
        var candidatesFromTrash = me.Trash.Where(card =>
            card.Info.Kind == CardKind.Character
            && card.Info.Cost == 1
            && !string.IsNullOrWhiteSpace(card.Info.Trigger)).ToList();
        var revive = await P136P157Helpers.ChooseUpToOne(
            ctx, ctx.OwnerIndex, "OwnTrashCharacter",
            "将废弃区中最多1张费用为1且拥有【触发】的角色卡登场", candidatesFromTrash);
        if (revive is not null && me.Trash.Contains(revive))
            await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, revive);
    }
}

/// <summary>P-151 斯摩格：弃1；海军领袖可追加休息咚，之后看5检索海军。</summary>
public sealed class P_151_Smoker : IScriptedEffect
{
    public string CardNumber => "P-151";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Hand.Count == 0
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "丢弃1张手牌，发动斯摩格的登场时效果？")) return;
        if (await OfficialCoverageHelpers.PayDiscardFromHand(ctx) is null) return;

        if (me.Leader.Info.HasKeyword("海军") && me.DonDeck.Count > 0)
        {
            int option = await ctx.Prompts.ChooseOption(
                ctx.OwnerIndex, "是否从咚!!卡组追加1张休息状态的咚!!？", ["不追加", "追加1张"]);
            if (option == 1 && me.Leader.Info.HasKeyword("海军"))
                AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        }

        await OfficialCoverageHelpers.LookTopPickAndBottom(
            ctx, 5, card => card.Info.HasKeyword("海军"),
            "公开其中最多1张拥有《海军》特征的卡牌并加入手牌");
    }
}

/// <summary>P-157 蒙奇·D·路飞：弃1后复活废弃区中费用不高于4的埃鲁巴夫角色。</summary>
public sealed class P_157_MonkeyDLuffy : IScriptedEffect
{
    public string CardNumber => "P-157";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Hand.Count == 0
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "丢弃1张手牌，发动蒙奇·D·路飞的登场时效果？")) return;
        if (await OfficialCoverageHelpers.PayDiscardFromHand(ctx) is null) return;
        var candidates = me.Trash.Where(card =>
            card.Info.Kind == CardKind.Character
            && card.Info.Cost <= 4
            && card.Info.HasKeyword("埃鲁巴夫")).ToList();
        var revive = await P136P157Helpers.ChooseUpToOne(
            ctx, ctx.OwnerIndex, "OwnTrashCharacter",
            "将废弃区中最多1张费用不高于4的《埃鲁巴夫》角色卡登场", candidates);
        if (revive is not null && me.Trash.Contains(revive))
            await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, revive);
    }
}
