using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects;

/// <summary>
/// 对历史 DSL 中明确写明“省略/忽略/未实现”的分句做组合式补齐。
/// 前置阶段负责旧 DSL 无法表达的成本、条件和持续注册；后置阶段复用 DSL 已选择并写入 ctx.Vars 的同一目标。
/// </summary>
public static class DeclaredOmissionEffects
{
    public static async Task<bool> BeforeDsl(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opponent = ctx.State.Players[1 - ctx.OwnerIndex];
        string number = ctx.Source.Info.Number;

        switch ((number, ctx.Trigger))
        {
            case ("EB01-027", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(),
                    Scope = OwnSourceScope(),
                    PowerDeltaResolver = (state, _, _) =>
                        state.Players[ctx.OwnerIndex].Trash.Count(card => card.Info.Kind == CardKind.Event) / 2 * 1000,
                    Predicate = (state, side, card) => side == ctx.OwnerIndex && card.Id == ctx.Source.Id
                        && state.Players[ctx.OwnerIndex].Leader.Info.HasKeyword("巴洛克工作室"),
                });
                break;

            case ("EB01-056", EffectTrigger.OnEnterField):
                if (!await PayLifeEdgeToHand(ctx, "夏洛特·弗朗蓓：将生命区最上方或最下方 1 张加入手牌并抽 1 张？"))
                    return false;
                break;

            case ("EB02-003", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(), Scope = OwnSourceScope(), PowerDelta = 2000,
                    Predicate = (state, side, card) => side == ctx.OwnerIndex && card.Id == ctx.Source.Id
                        && state.CurrentTurnPlayer != ctx.OwnerIndex
                        && state.Players[ctx.OwnerIndex].AttachedDonCount(ctx.Source.Id) >= 2,
                });
                break;

            case ("EB02-019", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(), Scope = OwnSourceScope(),
                    GrantKeyword = "登场回合可攻击角色",
                    Predicate = (state, side, card) => side == ctx.OwnerIndex && card.Id == ctx.Source.Id
                        && state.Players[1 - ctx.OwnerIndex].Characters.Count >= 2,
                });
                break;

            case ("EB03-054", EffectTrigger.OnEnterField):
                if (!await PayTopLifeToTrash(ctx, "妮古·罗宾：将生命区最上方 1 张放入废弃区并补 1 张生命？"))
                    return false;
                break;

            case ("EB04-001", EffectTrigger.OnGameStart):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(),
                    Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
                    PowerDelta = 2000,
                    Predicate = (state, side, card) => side == ctx.OwnerIndex && card.Id == ctx.Source.Id
                        && state.CurrentTurnPlayer != ctx.OwnerIndex && state.Players[ctx.OwnerIndex].LifeArea.Count <= 1,
                });
                return false;

            case ("EB04-001", EffectTrigger.ActivatedMain):
                await ResolveEB04_001(ctx);
                return false;

            case ("EB04-010", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(),
                    Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                    PowerDelta = 5000,
                    Predicate = (state, side, card) => side == ctx.OwnerIndex
                        && state.CurrentTurnPlayer != ctx.OwnerIndex && card.Info.Cost == 1,
                });
                break;

            case ("EB04-017", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(),
                    Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
                    CostDelta = -1,
                    Predicate = (state, side, _) => side == 1 - ctx.OwnerIndex
                        && state.CurrentTurnPlayer == ctx.OwnerIndex
                        && state.Players[ctx.OwnerIndex].Characters.Count(card => card.Info.HasKeyword("纯毛族")) >= 3,
                });
                break;

            case ("EB04-048", EffectTrigger.OnEnterField):
                RegisterRobLucci(ctx);
                await ResolveEB04_048(ctx);
                return false;

            case ("OP01-024", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(), Scope = OwnSourceScope(), KoGuard = "battle",
                    Predicate = (state, side, card) => side == ctx.OwnerIndex && card.Id == ctx.Source.Id
                        && state.Players[ctx.OwnerIndex].AttachedDonCount(ctx.Source.Id) >= 2
                        && BattleOpponent(state, ctx.Source.Id) is { } foe && HasProperty(foe, "打"),
                });
                break;

            case ("OP01-118", EffectTrigger.EventCounter):
                if (!await AtomicOps.PromptReturnDonToDeck(ctx, 2, optional: true)) return false;
                break;

            case ("OP03-078", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(),
                    Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
                    CostDelta = -3,
                    Predicate = (state, side, _) => side == 1 - ctx.OwnerIndex
                        && state.CurrentTurnPlayer == ctx.OwnerIndex
                        && state.Players[ctx.OwnerIndex].AttachedDonCount(ctx.Source.Id) >= 1,
                });
                break;

            case ("OP03-096", EffectTrigger.EventMain):
                await ResolveOP03_096(ctx);
                return false;

            case ("OP03-109", EffectTrigger.OnEnterField):
                if (!await PayLifeEdgeToTrash(ctx, "夏洛特·戚风蛋糕：将生命区最上方或最下方 1 张放入废弃区并补 1 张生命？"))
                    return false;
                break;

            case ("OP04-039", EffectTrigger.ActivatedMain):
                if (!await RestActiveDon(ctx, 1, "莉贝卡：将 1 张活跃咚转为休息状态以发动效果？")) return false;
                break;

            case ("OP04-041", EffectTrigger.OnEnterField):
                if (!await DiscardHandCost(ctx, 2, _ => true, "阿碧丝：丢弃 2 张手牌以发动登场效果？")) return false;
                break;

            case ("OP04-074", EffectTrigger.EventCounter):
            case ("OP04-076", EffectTrigger.EventCounter):
                if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1, optional: true)) return false;
                break;

            case ("OP04-082", EffectTrigger.PreKO):
                await ResolveOP04_082(ctx);
                return false;

            case ("OP05-093", EffectTrigger.OnEnterField):
                if (!await ReturnTrashCost(ctx, 3, "罗布·鲁兹：将废弃区 3 张卡牌放回卡组底以发动登场效果？")) return false;
                break;

            case ("OP05-101", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(), Scope = OwnSourceScope(), PowerDelta = 1000,
                    Predicate = (state, side, card) => side == ctx.OwnerIndex && card.Id == ctx.Source.Id
                        && state.Players[ctx.OwnerIndex].LifeArea.Count <= 2,
                });
                break;

            case ("OP06-033", EffectTrigger.OnEnterField):
                await ResolveOP06_033(ctx);
                return false;

            case ("OP06-115", EffectTrigger.OnLifeRevealTrigger):
                if (me.LifeArea.Count != 0) return false;
                break;

            case ("OP07-004", EffectTrigger.OnEnterField):
                if (!await DiscardHandCost(ctx, 1, _ => true, "加利·达丹：丢弃 1 张手牌以发动登场效果？")) return false;
                break;

            case ("OP07-056", EffectTrigger.EventCounter):
                await ResolveOP07_056Counter(ctx);
                return false;

            case ("OP07-071", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(),
                    Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
                    PowerDelta = -1000,
                    Predicate = (state, side, card) => side == 1 - ctx.OwnerIndex
                        && state.Players[side].Characters.Contains(card)
                        && state.CurrentTurnPlayer != ctx.OwnerIndex
                        && state.Players[ctx.OwnerIndex].Leader.Info.HasKeyword("福克斯海盗团"),
                });
                break;

            case ("OP07-075", EffectTrigger.EventCounter):
                await ResolveOP07_075(ctx);
                return false;

            case ("OP07-117", EffectTrigger.OnLifeRevealTrigger):
                if (me.Trash.Contains(ctx.Source)) await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
                return false;

            case ("OP08-075", EffectTrigger.EventMain):
                await ResolveOP08_075(ctx);
                return false;

            case ("OP08-084", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(), Scope = OwnSourceScope(), CostDelta = 4,
                    Predicate = (_, side, card) => side == ctx.OwnerIndex && card.Id == ctx.Source.Id,
                });
                break;

            case ("OP08-106", EffectTrigger.OnEnterField):
                await ResolveOP08_106(ctx);
                return false;

            case ("OP09-028", EffectTrigger.OnKO):
                await ResolveOP09_028(ctx);
                return false;

            case ("OP09-039", EffectTrigger.EventCounter):
                if (!me.Leader.Info.HasKeyword("时光旅诗") || me.Characters.Count(card => card.IsTapped) < 2)
                    return false;
                break;

            case ("OP13-080", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(), Scope = OwnSourceScope(), LeaveGuard = "effect",
                    GrantKeyword = "速攻",
                    Predicate = (state, side, card) => side == ctx.OwnerIndex && card.Id == ctx.Source.Id
                        && state.Players[ctx.OwnerIndex].Trash.Count >= 7,
                });
                return false;

            case ("OP13-083", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(), Scope = OwnSourceScope(), LeaveGuard = "effect",
                    Predicate = (state, side, card) => side == ctx.OwnerIndex && card.Id == ctx.Source.Id
                        && state.Players[ctx.OwnerIndex].Trash.Count >= 7,
                });
                break;

            case ("OP13-109", EffectTrigger.OnAllyWillLeaveField):
                await ResolveOP13_109(ctx);
                return false;

            case ("OP14-045", EffectTrigger.OnHandDiscarded):
            case ("OP14-049", EffectTrigger.OnHandDiscarded):
                if (PayloadOwner(ctx) == ctx.OwnerIndex)
                    AtomicOps.GiveKeyword(ctx.Source, "速攻", KeywordDuration.ThisTurn, ctx.OwnerIndex);
                return false;

            case ("OP14-049", EffectTrigger.OnEnterField):
                await ResolveOP14_049(ctx);
                return false;

            case ("OP14-090", EffectTrigger.OnEnterField):
            {
                var sourceId = ctx.Source.Id;
                int owner = ctx.OwnerIndex;
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = sourceId.ToString(),
                    Scope = OwnSourceScope(),
                    GrantKeyword = "速攻",
                    Predicate = (state, side, card) =>
                        side == owner
                        && card.Id == sourceId
                        && Enumerable.Range(0, state.Players.Length).Any(playerIndex =>
                            state.Players[playerIndex].Characters.Any(character =>
                            {
                                int currentCost = state.CurrentCostOf(playerIndex, character);
                                return currentCost == 0 || currentCost >= 8;
                            })),
                });
                break;
            }

            case ("OP15-060", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(), Scope = OwnSourceScope(), PowerDelta = 2000,
                    LeaveGuard = "effect",
                    Predicate = (state, side, card) => side == ctx.OwnerIndex && card.Id == ctx.Source.Id
                        && state.Players[ctx.OwnerIndex].TotalDonInCostArea <= 6,
                });
                return false;

            case ("OP15-067", EffectTrigger.OnEnterField):
                Register(ctx, new ContinuousEffect
                {
                    SourceCardId = ctx.Source.Id.ToString(), Scope = OwnSourceScope(), GrantKeyword = "速攻",
                    Predicate = (state, side, card) => side == ctx.OwnerIndex && card.Id == ctx.Source.Id
                        && state.Players[ctx.OwnerIndex].TotalDonInCostArea <= 6,
                });
                break;

            case ("OP15-095", EffectTrigger.EventCounter):
                if (me.Trash.Count < 15) return false;
                break;

            // ── 历史“近似实现”精确化 ──
            case ("OP02-048", EffectTrigger.ActivatedMain):
                await ResolveOP02_048(ctx);
                return false;

            case ("OP02-057", EffectTrigger.OnEnterField):
                await ResolveLookTopWithPlacement(ctx, 2,
                    card => card.Info.HasKeyword("王下七武海"), "公开最多 1 张《王下七武海》卡牌加入手牌");
                return false;

            case ("OP03-017", EffectTrigger.EventCounter):
            case ("OP03-017", EffectTrigger.OnLifeRevealTrigger):
                if (!me.Leader.Info.HasKeywordContaining("白胡子海盗团")) return false;
                break;

            case ("OP03-036", EffectTrigger.EventMain):
                await ResolveOP03_036(ctx);
                return false;

            case ("OP03-049", EffectTrigger.OnEnterField):
                if (me.Deck.Count > 20) return false;
                await BounceAnyCharacter(ctx, 3);
                return false;

            case ("OP03-121", EffectTrigger.EventMain):
                await ResolveOP03_121(ctx);
                return false;

            case ("OP03-122", EffectTrigger.OnEnterField):
                await ResolveOP03_122(ctx);
                return false;

            case ("OP04-091", EffectTrigger.OnEnterField):
                await ResolveOP04_091(ctx);
                return false;

            case ("OP04-115", EffectTrigger.EventMain):
                await ResolveOP04_115(ctx);
                return false;

            case ("OP05-059", EffectTrigger.EventMain):
                if (me.Leader.Info.ColorList.Length < 2) return false;
                AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
                await BounceAnyCharacter(ctx, 5);
                return false;

            case ("OP05-059", EffectTrigger.OnLifeRevealTrigger):
                if (me.Leader.Info.ColorList.Length < 2) return false;
                break;

            case ("OP07-104", EffectTrigger.OnLifeRevealTrigger):
            case ("OP07-113", EffectTrigger.OnLifeRevealTrigger):
                if (!me.Leader.Info.HasKeyword("艾格赫德")) return false;
                break;

            case ("OP07-107", EffectTrigger.OnLifeRevealTrigger):
                AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
                if (me.LifeArea.Count <= 1 && me.Trash.Contains(ctx.Source))
                    await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
                return false;

            case ("OP08-053", EffectTrigger.EventMain):
                if (!me.Leader.Info.HasKeywordContaining("白胡子海盗团")) return false;
                await ResolveLookTopWithPlacement(ctx, 3,
                    card => card.Info.HasKeywordContaining("白胡子海盗团") || card.MatchesName("蒙奇·D·路飞"),
                    "公开最多 1 张《白胡子海盗团》卡牌或“蒙奇·D·路飞”加入手牌");
                return false;

            case ("OP08-095", EffectTrigger.EventMain):
                if (me.Trash.Count < 10) return false;
                await AddPowerUntilOpponentEnd(ctx, 2000);
                return false;

            case ("OP08-103", EffectTrigger.ActivatedMain):
                await ResolveOP08_103(ctx);
                return false;

            case ("OP09-078", EffectTrigger.EventCounter):
                await ResolveOP09_078(ctx);
                return false;

            case ("OP09-098", EffectTrigger.EventMain):
                await ResolveOP09_098(ctx);
                return false;

            case ("OP09-101", EffectTrigger.OnEnterField):
                await ResolveOP09_101(ctx);
                return false;

            case ("OP11-040", EffectTrigger.OnTurnStart):
                if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex || me.TotalDonInCostArea < 8
                    || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                        "蒙奇·D·路飞：确认卡组顶 5 张并公开最多 1 张《草帽一伙》卡牌加入手牌？")) return false;
                await ResolveLookTopWithPlacement(ctx, 5, card => card.Info.HasKeyword("草帽一伙"),
                    "公开最多 1 张《草帽一伙》卡牌加入手牌");
                return false;

            case ("OP14-096", EffectTrigger.EventCounter):
                if (me.Trash.Count < 10) return false;
                break;

            case ("OP15-103", EffectTrigger.OnLifeRevealTrigger):
                AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
                if (me.LifeArea.Count <= 2 && me.Trash.Contains(ctx.Source))
                    await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
                return false;

            case ("OP15-116", EffectTrigger.EventMain):
                await ResolveOP15_116(ctx);
                return false;

            case ("PRB02-007", EffectTrigger.OnAttackDeclare):
                await ReturnAnyCharacterToDeckBottom(ctx, 1);
                return false;

            case ("PRB02-018", EffectTrigger.OnEnterField):
                if (!me.LifeArea.Any(card => card.IsLifeFaceUp)) return false;
                break;

            case ("PRB02-016", EffectTrigger.ActivatedMain):
                await ResolvePRB02_016(ctx);
                return false;
        }

        return true;
    }

    public static async Task AfterDsl(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        string number = ctx.Source.Info.Number;
        switch ((number, ctx.Trigger))
        {
            case ("EB02-021", EffectTrigger.EventMain):
                if (VarCard(ctx, "$tgt") is { } strawHat) AtomicOps.PreventActivateNextReset(strawHat);
                break;
            case ("EB03-020", EffectTrigger.EventCounter):
                if (me.Characters.Count(card => card.Info.HasKeyword("FILM")) >= 2 && VarCard(ctx, "$t") is { } filmTarget)
                    AtomicOps.AddPowerThisBattle(filmTarget, 2000);
                break;
            case ("OP01-029", EffectTrigger.EventCounter):
                if (me.LifeArea.Count <= 2 && VarCard(ctx, "$t") is { } ionTarget)
                    AtomicOps.AddPowerThisBattle(ionTarget, 2000);
                break;
            case ("OP01-119", EffectTrigger.EventCounter):
                if (me.LifeArea.Count <= 2) AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
                break;
            case ("OP04-075", EffectTrigger.EventCounter):
                if (me.LifeArea.Count <= 2) AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
                break;
            case ("OP07-035", EffectTrigger.EventCounter):
                if (me.Characters.Count >= 3 && VarCard(ctx, "$t") is { } karmaTarget)
                    AtomicOps.AddPowerThisBattle(karmaTarget, 1000);
                break;
            case ("OP07-056", EffectTrigger.OnLifeRevealTrigger):
                await ReturnHandToBottom(ctx, 2);
                break;
            case ("OP07-057", EffectTrigger.EventMain):
                if (VarCard(ctx, "$t") is { } perfumeTarget)
                    AtomicOps.GiveKeyword(perfumeTarget, "不可阻挡", KeywordDuration.ThisTurn, ctx.OwnerIndex);
                break;
            case ("OP08-076", EffectTrigger.EventMain):
                if (ctx.State.Players[1 - ctx.OwnerIndex].Characters.Any(card =>
                        ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, card) >= 6000))
                    AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
                break;
            case ("OP10-097", EffectTrigger.EventMain):
                if (me.Trash.Count >= 10 && VarCard(ctx, "$tgt") is { } rhinoTarget)
                    AtomicOps.GiveKeyword(rhinoTarget, "流放", KeywordDuration.ThisTurn, ctx.OwnerIndex);
                break;
            case ("OP12-109", EffectTrigger.OnLifeRevealTrigger):
                if (me.Trash.Remove(ctx.Source)) me.Hand.Add(ctx.Source);
                break;
        }
    }

    private static void Register(EffectContext ctx, params ContinuousEffect[] effects)
    {
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == ctx.Source.Id.ToString());
        ctx.State.ContinuousEffects.AddRange(effects);
    }

    private static ContinuousScope OwnSourceScope()
        => new() { Side = 0, IncludeLeader = true, IncludeCharacters = true };

    private static bool HasProperty(CardInstance card, string property)
        => card.HasProperty(property);

    private static CardInstance? BattleOpponent(GameState state, Guid sourceId)
    {
        var battle = state.CurrentBattle;
        if (battle is null) return null;
        if (battle.AttackerCardId == sourceId)
        {
            var defender = state.Players[battle.DefenderPlayerIndex];
            if (battle.TargetIsLeader) return defender.Leader;
            var targetId = battle.ReplacedByBlockerCardId ?? battle.TargetCardId;
            return targetId is null ? null : defender.Characters.FirstOrDefault(card => card.Id == targetId.Value);
        }
        var defended = battle.ReplacedByBlockerCardId ?? battle.TargetCardId;
        if (defended != sourceId) return null;
        var attacker = state.Players[battle.AttackerPlayerIndex];
        return attacker.Leader.Id == battle.AttackerCardId
            ? attacker.Leader : attacker.Characters.FirstOrDefault(card => card.Id == battle.AttackerCardId);
    }

    private static CardInstance? VarCard(EffectContext ctx, string key)
        => ctx.Vars.TryGetValue(key, out var value) ? value as CardInstance : null;

    private static int PayloadOwner(EffectContext ctx)
        => ctx.Vars.TryGetValue("owner", out var value) && value is int owner ? owner : -1;

    private static async Task<CardInstance?> ChooseUpToOne(
        EffectContext ctx, string kind, string text, IReadOnlyList<CardInstance> candidates,
        IReadOnlyList<CardInstance>? displayedCards = null)
    {
        if (candidates.Count == 0) return null;
        var selected = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, kind, text,
            candidates.Select(card => card.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = (displayedCards ?? candidates)
                    .Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        return selected.Count == 0 ? null
            : candidates.FirstOrDefault(card => card.Id.ToString() == selected[0]);
    }

    private static async Task<bool> RestActiveDon(EffectContext ctx, int count, string prompt)
    {
        var active = ctx.State.Players[ctx.OwnerIndex].CostArea.Where(don => don.State == DonState.Active).ToList();
        if (active.Count < count) return false;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, prompt)) return false;
        var selected = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RestOwnDon", $"选择 {count} 张活跃咚转为休息状态",
            active.Select(don => don.Id.ToString()).ToList(), count, count,
            new Dictionary<string, object?>
            {
                ["donChoices"] = active.Select(don => new { id = don.Id.ToString(), state = don.State.ToString() }).ToList(),
            });
        if (selected.Count < count) return false;
        foreach (var id in selected.Take(count))
        {
            var don = active.FirstOrDefault(item => item.Id.ToString() == id);
            if (don is not null) don.State = DonState.Rest;
        }
        return true;
    }

    private static async Task<bool> DiscardHandCost(
        EffectContext ctx, int count, Func<CardInstance, bool> predicate, string prompt)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var candidates = me.Hand.Where(predicate).ToList();
        if (candidates.Count < count || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, prompt)) return false;
        var selected = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard", $"选择丢弃的 {count} 张手牌",
            candidates.Select(card => card.Id.ToString()).ToList(), count, count,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = candidates.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        if (selected.Count < count) return false;
        EffectRuntime.PayingCost = true;
        try
        {
            foreach (var id in selected.Take(count))
            {
                var card = me.Hand.FirstOrDefault(item => item.Id.ToString() == id);
                if (card is not null) AtomicOps.DiscardHand(me, card);
            }
        }
        finally { EffectRuntime.PayingCost = false; }
        return true;
    }

    private static async Task<bool> PayLifeEdgeToHand(EffectContext ctx, string prompt)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.LifeArea.Count == 0 || ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return false;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, prompt)) return false;
        int edge = me.LifeArea.Count == 1 ? 0 : await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "选择生命区位置", new[] { "最上方", "最下方" });
        var card = edge == 0 ? me.LifeArea[0] : me.LifeArea[^1];
        me.LifeArea.Remove(card);
        card.IsLifeFaceUp = false;
        me.Hand.Add(card);
        return true;
    }

    private static async Task<bool> PayLifeEdgeToTrash(EffectContext ctx, string prompt)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.LifeArea.Count == 0 || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, prompt)) return false;
        int edge = me.LifeArea.Count == 1 ? 0 : await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "选择生命区位置", new[] { "最上方", "最下方" });
        var card = edge == 0 ? me.LifeArea[0] : me.LifeArea[^1];
        me.LifeArea.Remove(card);
        card.IsLifeFaceUp = false;
        me.Trash.Add(card);
        return true;
    }

    private static async Task<bool> PayTopLifeToTrash(EffectContext ctx, string prompt)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.LifeArea.Count == 0 || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, prompt)) return false;
        var card = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        card.IsLifeFaceUp = false;
        me.Trash.Add(card);
        return true;
    }

    private static async Task<bool> ReturnTrashCost(EffectContext ctx, int count, string prompt)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Trash.Count < count || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, prompt)) return false;
        var selected = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Trash",
            $"按选择顺序将废弃区 {count} 张卡牌放回卡组最下方",
            me.Trash.Select(card => card.Id.ToString()).ToList(), count, count,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Trash.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        if (selected.Count < count) return false;
        foreach (var id in selected.Take(count))
        {
            var card = me.Trash.FirstOrDefault(item => item.Id.ToString() == id);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(me, card);
        }
        return true;
    }

    private static async Task ReturnHandToBottom(EffectContext ctx, int count)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int actual = Math.Min(count, me.Hand.Count);
        if (actual == 0) return;
        var selected = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            $"按选择顺序将 {actual} 张手牌放回卡组最下方",
            me.Hand.Select(card => card.Id.ToString()).ToList(), actual, actual,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        foreach (var id in selected.Take(actual))
        {
            var card = me.Hand.FirstOrDefault(item => item.Id.ToString() == id);
            if (card is not null) AtomicOps.ReturnHandToDeckBottom(me, card);
        }
    }

    private static async Task ResolveEB04_001(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        string key = $"{ctx.Source.Id}-Activated";
        if (me.TurnOnceUsed.Contains(key)) return;
        var target = await ChooseUpToOne(ctx, "OpponentCharacter", "选择对方最多 1 张角色，本回合力量-1000",
            ctx.State.Players[1 - ctx.OwnerIndex].Characters);
        if (target is not null) target.PowerModThisTurn -= 1000;
        if (me.LifeArea.Count >= 2 && !ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)
            && await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将我方生命区最上方 1 张卡牌加入手牌？"))
        {
            var life = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            life.IsLifeFaceUp = false;
            me.Hand.Add(life);
        }
        me.TurnOnceUsed.Add(key);
    }

    private static void RegisterRobLucci(EffectContext ctx)
    {
        Func<GameState, int, CardInstance, bool> predicate = (state, side, card) =>
            side == ctx.OwnerIndex && card.Id == ctx.Source.Id
            && state.Players[ctx.OwnerIndex].Leader.Info.HasKeywordContaining("CP");
        Register(ctx,
            new ContinuousEffect
            {
                SourceCardId = ctx.Source.Id.ToString(), Scope = OwnSourceScope(), Predicate = predicate,
                PowerDeltaResolver = (state, _, _) => state.Players[ctx.OwnerIndex].Trash.Count / 5 * 1000,
            },
            new ContinuousEffect
            {
                SourceCardId = ctx.Source.Id.ToString(), Scope = OwnSourceScope(), Predicate = predicate,
                CostDeltaResolver = (state, _, _) => state.Players[ctx.OwnerIndex].Trash.Count / 5 * 2,
            });
    }

    private static async Task ResolveEB04_048(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Characters.Count == 0
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "罗布·鲁兹：将我方 1 张角色放入废弃区并抽 1 张？")) return;
        var cost = await ChooseUpToOne(ctx, "OwnCharacter", "选择放入废弃区的 1 张角色", me.Characters);
        if (cost is null) return;
        SendOwnCharacterToTrash(me, cost);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }

    private static void SendOwnCharacterToTrash(PlayerState owner, CardInstance card)
    {
        foreach (var don in owner.CostArea.Where(don => don.State == DonState.Attached && don.AttachedToCardId == card.Id))
        {
            don.State = DonState.Rest;
            don.AttachedToCardId = null;
        }
        if (owner.Characters.Remove(card)) owner.Trash.Add(card);
    }

    private static async Task ResolveOP03_096(EffectContext ctx)
    {
        int opponentIndex = 1 - ctx.OwnerIndex;
        var opponent = ctx.State.Players[opponentIndex];
        var candidates = opponent.Characters.Where(card => ctx.State.CurrentCostOf(opponentIndex, card) == 0).ToList();
        if (opponent.StageCard is { } stage && ctx.State.CurrentCostOf(opponentIndex, stage) <= 3) candidates.Add(stage);
        var target = await ChooseUpToOne(ctx, "OpponentCharacterOrStage",
            "选择对方最多 1 张费用为 0 的角色或费用不高于 3 的舞台 KO", candidates);
        if (target is not null)
            await AtomicOps.KOByEffectAsync(ctx.State, opponentIndex, target, ctx.Prompts, ctx.OwnerIndex);
    }

    private static async Task ResolveOP04_082(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var costs = new List<CardInstance>();
        if (!me.Leader.IsTapped) costs.Add(me.Leader);
        if (me.StageCard is { IsTapped: false } stage && stage.MatchesName("斗牛竞技场")) costs.Add(stage);
        if (costs.Count == 0 || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "居鲁士：将领袖或“斗牛竞技场”转为休息状态，代替此角色被 KO？")) return;
        var selected = await ChooseUpToOne(ctx, "OwnLeaderOrStage", "选择转为休息状态的卡牌", costs);
        if (selected is null) return;
        AtomicOps.RestCard(selected);
        ctx.State.MarkPreventKO(ctx.Source.Id);
    }

    private static async Task ResolveOP06_033(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var fishmen = me.Hand.Where(card => card.Info.HasKeyword("鱼人族")).ToList();
        var noahs = me.Hand.Where(card => card.MatchesName("方舟诺亚")).ToList();
        if (me.StageCard is { } stage && stage.MatchesName("方舟诺亚")) noahs.Add(stage);
        if (fishmen.Count == 0 && noahs.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "范德·戴肯九世：支付 1 张《鱼人族》手牌或“方舟诺亚”以发动？")) return;
        var allCosts = fishmen.Concat(noahs).DistinctBy(card => card.Id).ToList();
        var cost = await ChooseUpToOne(ctx, "OwnHandOrStage", "选择支付的卡牌", allCosts);
        if (cost is null) return;
        EffectRuntime.PayingCost = true;
        try
        {
            if (me.Hand.Contains(cost)) AtomicOps.DiscardHand(me, cost);
            else if (ReferenceEquals(me.StageCard, cost))
            {
                me.StageCard = null;
                me.Trash.Add(cost);
            }
        }
        finally { EffectRuntime.PayingCost = false; }
        var targets = ctx.State.Players[1 - ctx.OwnerIndex].Characters.Where(card => card.IsTapped).ToList();
        var target = await ChooseUpToOne(ctx, "OpponentRestingCharacter", "选择对方最多 1 张休息角色 KO", targets);
        if (target is not null)
            await AtomicOps.KOByEffectAsync(ctx.State, 1 - ctx.OwnerIndex, target, ctx.Prompts, ctx.OwnerIndex);
    }

    private static async Task ResolveOP07_056Counter(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var costs = me.Characters.Where(card => ctx.State.CurrentCostOf(ctx.OwnerIndex, card) >= 2).ToList();
        if (costs.Count == 0 || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "虏之矢：将我方 1 张费用不低于 2 的角色放回手牌，使我方卡牌本次战斗+4000？")) return;
        var cost = await ChooseUpToOne(ctx, "OwnCharacter", "选择放回手牌的角色", costs);
        if (cost is null) return;
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, cost);
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var target = await ChooseUpToOne(ctx, "OwnLeaderOrCharacter", "选择我方最多 1 张领袖或角色+4000", targets);
        if (target is not null) AtomicOps.AddPowerThisBattle(target, 4000);
    }

    private static async Task ResolveOP07_075(EffectContext ctx)
    {
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1, optional: true)) return;
        var opponent = ctx.State.Players[1 - ctx.OwnerIndex];
        var leader = await ChooseUpToOne(ctx, "OpponentLeader", "选择对方最多 1 张领袖，本回合力量-2000",
            new[] { opponent.Leader });
        if (leader is not null) leader.PowerModThisTurn -= 2000;
        var character = await ChooseUpToOne(ctx, "OpponentCharacter", "选择对方最多 1 张角色，本回合力量-2000",
            opponent.Characters);
        if (character is not null) character.PowerModThisTurn -= 2000;
    }

    private static async Task ResolveOP08_075(EffectContext ctx)
    {
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1, optional: true)) return;
        int opponent = 1 - ctx.OwnerIndex;
        var targets = ctx.State.Players[opponent].Characters
            .Where(card => ctx.State.CurrentCostOf(opponent, card) <= 2).ToList();
        var target = await ChooseUpToOne(ctx, "OpponentCharacter", "选择对方最多 1 张费用不高于 2 的角色转为休息状态", targets);
        if (target is not null) AtomicOps.RestCard(target);
        AtomicOps.FlipAllLifeFaceDown(ctx.State.Players[ctx.OwnerIndex]);
    }

    private static async Task ResolveOP08_106(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var costs = me.Hand.Where(card => !string.IsNullOrEmpty(card.Info.Trigger)).ToList();
        if (costs.Count == 0 || !await DiscardHandCost(ctx, 1, card => !string.IsNullOrEmpty(card.Info.Trigger),
                "奈美：丢弃 1 张拥有【触发】的手牌以发动登场效果？")) return;
        int opponent = 1 - ctx.OwnerIndex;
        var targets = ctx.State.Players[opponent].Characters
            .Where(card => ctx.State.CurrentCostOf(opponent, card) <= 5).ToList();
        var target = await ChooseUpToOne(ctx, "OpponentCharacter", "选择对方最多 1 张费用不高于 5 的角色 KO", targets);
        if (target is not null)
            await AtomicOps.KOByEffectAsync(ctx.State, opponent, target, ctx.Prompts, ctx.OwnerIndex);
        if (me.Hand.Count <= 3) AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }

    private static async Task ResolveOP09_028(EffectContext ctx)
    {
        if (!await PayLifeEdgeToHand(ctx, "山智【KO时】：将生命区最上方或最下方 1 张加入手牌以发动？")) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        var targets = me.Trash.Where(card => card.Info.Kind == CardKind.Character && card.Info.Cost <= 4
            && (card.Info.HasKeyword("时光旅诗") || card.Info.HasKeyword("草帽一伙"))).ToList();
        var target = await ChooseUpToOne(ctx, "Trash", "选择最多 1 张符合条件的角色以休息状态登场", targets);
        if (target is not null) await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, target, restState: true);
    }

    private static async Task ResolveOP13_109(EffectContext ctx)
    {
        if (!ctx.Vars.TryGetValue("victimId", out var victim)
            || victim?.ToString() != ctx.Source.Id.ToString()) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.LifeArea.Count == 0 || me.LifeArea[0].IsLifeFaceUp) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "杰丽·邦妮：将生命区最上方 1 张翻至正面，使此角色不离场？")) return;
        me.LifeArea[0].IsLifeFaceUp = true;
        ctx.State.MarkPreventLeave(ctx.Source.Id);
    }

    private static async Task ResolveOP14_049(EffectContext ctx)
    {
        if (!await RestActiveDon(ctx, 2, "甚平：将 2 张活跃咚转为休息状态以发动登场效果？")) return;
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
        var candidates = new List<(int Owner, CardInstance Card)>();
        for (int owner = 0; owner < 2; owner++)
            candidates.AddRange(ctx.State.Players[owner].Characters
                .Where(card => ctx.State.CurrentCostOf(owner, card) <= 7).Select(card => (owner, card)));
        var target = await ChooseUpToOne(ctx, "AnyCharacter", "选择最多 1 张费用不高于 7 的角色放回持有者手牌",
            candidates.Select(item => item.Card).ToList());
        if (target is null) return;
        var selected = candidates.First(item => item.Card.Id == target.Id);
        if (!await AtomicOps.TryEffectLeaveGuard(ctx.State, selected.Owner, target, ctx.Prompts, "hand"))
            AtomicOps.BounceToHand(ctx.State, selected.Owner, target);
    }

    private static async Task ResolveLookTopWithPlacement(
        EffectContext ctx, int count, Func<CardInstance, bool> predicate, string prompt)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var looked = me.Deck.Take(count).ToList();
        if (looked.Count == 0) return;

        var candidates = looked.Where(predicate).ToList();
        // 检索时 validChoices 只包含合法目标，但 choiceCards 必须携带确认到的全部牌，
        // 让玩家先看完整牌面，再从其中选择可检索卡；不合法目标由客户端置灰展示。
        var picked = await ChooseUpToOne(ctx, "LookTopReveal", prompt, candidates, looked);
        if (picked is not null)
        {
            ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new[] { picked.Info.Number });
            me.Deck.Remove(picked);
            me.Hand.Add(picked);
            looked.Remove(picked);
        }

        if (looked.Count == 0) return;
        var orderedIds = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OrderDeckCards",
            "将剩余卡牌自选顺序排列（先选择的卡牌最靠近卡组顶或最先进入卡组底）",
            looked.Select(card => card.Id.ToString()).ToList(), looked.Count, looked.Count,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = looked.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        var ordered = orderedIds.Select(id => looked.FirstOrDefault(card => card.Id.ToString() == id))
            .Where(card => card is not null).Cast<CardInstance>().ToList();
        foreach (var card in looked)
            if (!ordered.Contains(card)) ordered.Add(card);

        int placement = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "选择剩余卡牌放置位置", new[] { "卡组最上方", "卡组最下方" });
        foreach (var card in looked) me.Deck.Remove(card);
        if (placement == 0) me.Deck.InsertRange(0, ordered);
        else me.Deck.AddRange(ordered);
    }

    private static async Task ResolveOP02_048(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var discardable = me.Hand.Where(card => card.Info.HasKeyword("和之国")).ToList();
        if (ctx.Source.IsTapped || ctx.Source.HasRestriction(RestrictionKind.CannotBeRested)
            || discardable.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "选择丢弃的 1 张《和之国》手牌，或取消发动",
            discardable.Select(card => card.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = discardable.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        var discard = chosen.Count == 0 ? null
            : discardable.FirstOrDefault(card => card.Id.ToString() == chosen[0]);
        if (discard is null) return;

        AtomicOps.RestCard(ctx.Source);
        EffectRuntime.PayingCost = true;
        try { AtomicOps.DiscardHand(me, discard); }
        finally { EffectRuntime.PayingCost = false; }

        var restedDon = me.CostArea.Where(don => don.State == DonState.Rest && don.AttachedToCardId is null).ToList();
        if (restedDon.Count == 0) return;
        var donIds = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnRestDon",
            "选择最多 1 张休息状态的咚!!转为活跃状态",
            restedDon.Select(don => don.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?>
            {
                ["donChoices"] = restedDon.Select(don => new { id = don.Id.ToString(), state = don.State.ToString() }).ToList(),
            });
        if (donIds.Count > 0)
        {
            var don = restedDon.FirstOrDefault(item => item.Id.ToString() == donIds[0]);
            if (don is not null) don.State = DonState.Active;
        }
    }

    private static async Task ResolveOP03_036(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var restCosts = me.Characters.Where(card => !card.IsTapped && card.Info.HasKeyword("东海")).ToList();
        var kuros = me.Characters.Where(card => card.IsTapped && card.MatchesName("克洛")).ToList();
        if (restCosts.Count == 0 || kuros.Count == 0
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "杓死：将我方 1 张《东海》角色转为休息状态，使最多 1 张“克洛”转为活跃状态？")) return;
        var cost = await ChooseUpToOne(ctx, "OwnCharacter", "选择转为休息状态的《东海》角色", restCosts);
        if (cost is null) return;
        AtomicOps.RestCard(cost);
        var target = await ChooseUpToOne(ctx, "OwnCharacter", "选择最多 1 张“克洛”转为活跃状态", kuros);
        if (target is not null) AtomicOps.ActivateCard(target);
    }

    private static async Task BounceAnyCharacter(EffectContext ctx, int maxCost)
    {
        var candidates = new List<(int Owner, CardInstance Card)>();
        for (int owner = 0; owner < 2; owner++)
            candidates.AddRange(ctx.State.Players[owner].Characters
                .Where(card => ctx.State.CurrentCostOf(owner, card) <= maxCost)
                .Select(card => (owner, card)));
        var target = await ChooseUpToOne(ctx, "AnyCharacter",
            $"选择最多 1 张费用不高于 {maxCost} 的角色放回持有者手牌",
            candidates.Select(item => item.Card).ToList());
        if (target is null) return;
        var selected = candidates.First(item => item.Card.Id == target.Id);
        if (!await AtomicOps.TryEffectLeaveGuard(ctx.State, selected.Owner, target, ctx.Prompts, "hand"))
            AtomicOps.BounceToHand(ctx.State, selected.Owner, target);
    }

    private static async Task ResolveOP03_121(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.LifeArea.Count == 0
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "雷霆：将生命区最上方 1 张放入废弃区，将对方最多 1 张费用不高于 5 的角色 KO？")) return;
        var life = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        life.IsLifeFaceUp = false;
        me.Trash.Add(life);
        int opponent = 1 - ctx.OwnerIndex;
        var targets = ctx.State.Players[opponent].Characters
            .Where(card => ctx.State.CurrentCostOf(opponent, card) <= 5).ToList();
        var target = await ChooseUpToOne(ctx, "OpponentCharacter",
            "选择对方最多 1 张费用不高于 5 的角色 KO", targets);
        if (target is not null)
            await AtomicOps.KOByEffectAsync(ctx.State, opponent, target, ctx.Prompts, ctx.OwnerIndex);
    }

    private static async Task ResolveOP03_122(EffectContext ctx)
    {
        await BounceAnyCharacter(ctx, 6);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
        await DiscardOwnChosenMandatory(ctx, 2);
    }

    private static async Task ResolveOP04_091(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Leader.IsTapped
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "雷欧：将我方领袖转为休息状态，将对方最多 1 张费用不高于 1 的角色 KO，之后废弃卡组顶 2 张？")) return;
        AtomicOps.RestCard(me.Leader);
        if (me.Leader.Info.HasKeyword("德莱斯罗兹"))
        {
            int opponent = 1 - ctx.OwnerIndex;
            var targets = ctx.State.Players[opponent].Characters
                .Where(card => ctx.State.CurrentCostOf(opponent, card) <= 1).ToList();
            var target = await ChooseUpToOne(ctx, "OpponentCharacter",
                "选择对方最多 1 张费用不高于 1 的角色 KO", targets);
            if (target is not null)
                await AtomicOps.KOByEffectAsync(ctx.State, opponent, target, ctx.Prompts, ctx.OwnerIndex);
        }
        AtomicOps.MillTop(me, 2);
    }

    private static async Task ResolveOP04_115(EffectContext ctx)
    {
        if (!await PayLifeEdgeToHand(ctx,
                "枪·拟鬼：将生命区最上方或最下方 1 张加入手牌，使我方最多 1 张《和之国》角色获得双重攻击？")) return;
        var me = ctx.State.Players[ctx.OwnerIndex];
        var targets = me.Characters.Where(card => card.Info.HasKeyword("和之国")).ToList();
        var target = await ChooseUpToOne(ctx, "OwnCharacter",
            "选择我方最多 1 张《和之国》角色获得双重攻击", targets);
        if (target is not null)
            AtomicOps.GiveKeyword(target, "双重攻击", KeywordDuration.ThisTurn, ctx.OwnerIndex);
    }

    private static async Task AddPowerUntilOpponentEnd(EffectContext ctx, int delta)
    {
        var target = await ChooseUpToOne(ctx, "OwnCharacter",
            $"选择我方最多 1 张角色，直到下个对方回合结束时力量+{delta}",
            ctx.State.Players[ctx.OwnerIndex].Characters);
        if (target is not null) AtomicOps.AddPowerUntilOppEnd(target, delta, ctx.OwnerIndex);
    }

    private static async Task ResolveOP08_103(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        string key = $"{ctx.Source.Id}-Activated";
        if (me.TurnOnceUsed.Contains(key) || me.LifeArea.Count == 0
            || ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "夏洛特·卡仕达：将生命区最上方 1 张加入手牌，使我方最多 1 张角色直到下个对方回合结束时力量+1000？")) return;
        var life = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        life.IsLifeFaceUp = false;
        me.Hand.Add(life);
        await AddPowerUntilOpponentEnd(ctx, 1000);
        me.TurnOnceUsed.Add(key);
    }

    private static async Task ResolveOP09_078(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Leader.Info.HasKeyword("草帽一伙") || me.TotalDonInCostArea < 2 || me.Hand.Count == 0
            || !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "橡皮橡皮巨人：咚!!-2 并丢弃 1 张手牌，令我方最多 1 张领袖或角色本次战斗+4000，之后抽 2 张？")) return;
        var discard = await ChooseUpToOne(ctx, "OwnHandDiscard", "选择丢弃的 1 张手牌", me.Hand);
        if (discard is null || !await AtomicOps.PromptReturnDonToDeck(ctx, 2, optional: false)) return;
        EffectRuntime.PayingCost = true;
        try { AtomicOps.DiscardHand(me, discard); }
        finally { EffectRuntime.PayingCost = false; }

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var target = await ChooseUpToOne(ctx, "OwnLeaderOrCharacter",
            "选择我方最多 1 张领袖或角色本次战斗力量+4000", targets);
        if (target is not null) AtomicOps.AddPowerThisBattle(target, 4000);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
    }

    private static async Task ResolveOP09_098(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Leader.Info.HasKeyword("黑胡子海盗团")) return;
        int opponent = 1 - ctx.OwnerIndex;
        var target = await ChooseUpToOne(ctx, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合效果无效；若当前费用不高于 4，再将其 KO",
            ctx.State.Players[opponent].Characters);
        if (target is null) return;
        target.IsEffectsNullified = true;
        if (ctx.State.CurrentCostOf(opponent, target) <= 4)
            await AtomicOps.KOByEffectAsync(ctx.State, opponent, target, ctx.Prompts, ctx.OwnerIndex);
    }

    private static async Task ResolveOP09_101(EffectContext ctx)
    {
        int opponent = 1 - ctx.OwnerIndex;
        var targets = ctx.State.Players[opponent].Characters
            .Where(card => ctx.State.CurrentCostOf(opponent, card) <= 3).ToList();
        if (targets.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方 1 张费用不高于 3 的角色正面朝上放入其生命区",
            targets.Select(card => card.Id.ToString()).ToList(), 1, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = targets.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        var target = chosen.Count == 0 ? null : targets.FirstOrDefault(card => card.Id.ToString() == chosen[0]);
        if (target is null) return;
        int edge = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "选择放入对方生命区的位置", new[] { "最上方", "最下方" });
        if (await AtomicOps.TryEffectLeaveGuard(ctx.State, opponent, target, ctx.Prompts, "life")) return;
        AtomicOps.MoveCharToLife(ctx.State, opponent, target, toTop: edge == 0);
        target.IsLifeFaceUp = true;
        await AtomicOps.OpponentDiscardChosen(ctx.State, ctx.Prompts, opponent, 1);
    }

    private static async Task ResolveOP15_116(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (!me.Leader.Info.HasKeyword("草帽一伙")) return;

        // 各句按卡面顺序独立执行：生命区为空时只跳过“生命顶送废弃”，
        // 不能把后续“卡组顶加入生命”和“弃 1 手牌”一并取消。
        if (me.LifeArea.Count > 0)
        {
            var life = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            life.IsLifeFaceUp = false;
            me.Trash.Add(life);
        }
        AtomicOps.AddLifeFromDeckTop(me, 1);
        if (me.Hand.Count == 0) return;
        var discardIds = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard", "选择丢弃的 1 张手牌",
            me.Hand.Select(card => card.Id.ToString()).ToList(), 1, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        var discard = discardIds.Count == 0 ? null : me.Hand.FirstOrDefault(card => card.Id.ToString() == discardIds[0]);
        if (discard is not null) AtomicOps.DiscardHand(me, discard);
    }

    private static async Task ReturnAnyCharacterToDeckBottom(EffectContext ctx, int maxCost)
    {
        var candidates = new List<(int Owner, CardInstance Card)>();
        for (int owner = 0; owner < 2; owner++)
            candidates.AddRange(ctx.State.Players[owner].Characters
                .Where(card => ctx.State.CurrentCostOf(owner, card) <= maxCost)
                .Select(card => (owner, card)));
        var target = await ChooseUpToOne(ctx, "AnyCharacter",
            $"选择最多 1 张费用不高于 {maxCost} 的角色放回持有者卡组最下方",
            candidates.Select(item => item.Card).ToList());
        if (target is null) return;
        var selected = candidates.First(item => item.Card.Id == target.Id);
        if (!await AtomicOps.TryEffectLeaveGuard(ctx.State, selected.Owner, target, ctx.Prompts, "deck"))
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, selected.Owner, target);
    }

    private static async Task ResolvePRB02_016(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Source.IsTapped || ctx.Source.HasRestriction(RestrictionKind.CannotBeRested)
            || me.LifeArea.Count == 0 || ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return;
        int edge = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将生命区 1 张卡牌加入手牌作为成本", new[] { "最上方", "最下方", "放弃" });
        if (edge is < 0 or > 1) return;
        AtomicOps.RestCard(ctx.Source);
        int index = edge == 0 ? 0 : me.LifeArea.Count - 1;
        var life = me.LifeArea[index];
        me.LifeArea.RemoveAt(index);
        life.IsLifeFaceUp = false;
        me.Hand.Add(life);
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var target = await ChooseUpToOne(ctx, "OwnLeaderOrCharacter",
            "选择我方最多 1 张领袖或角色，本回合力量+3000", targets);
        if (target is not null) AtomicOps.AddPowerThisTurn(target, 3000);
    }

    private static async Task DiscardOwnChosenMandatory(EffectContext ctx, int count)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int actual = Math.Min(count, me.Hand.Count);
        if (actual == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            $"选择丢弃的 {actual} 张手牌", me.Hand.Select(card => card.Id.ToString()).ToList(), actual, actual,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        var ids = chosen.Count >= actual ? chosen.Take(actual).ToList()
            : me.Hand.Take(actual).Select(card => card.Id.ToString()).ToList();
        foreach (var id in ids)
        {
            var card = me.Hand.FirstOrDefault(item => item.Id.ToString() == id);
            if (card is not null) AtomicOps.DiscardHand(me, card);
        }
    }
}
