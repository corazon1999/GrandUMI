using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;

namespace GrandUMI.Effects;

/// <summary>
/// 原子效果库：覆盖 OP15 约 80% 的效果文本所需的最小操作集
/// 所有方法对 GameState 直接修改，不抛异常（执行失败按"无法解决"处理）
/// </summary>
public static class AtomicOps
{
    // ── 抽 & 丢 ──────────────────────────────────────────────────────────

    public static int Draw(GameState s, int playerIdx, int n)
    {
        int drew = TurnEngine.DrawCard(s, playerIdx, n);
        // 效果内抽牌(有环境上下文)=抽卡阶段以外抽牌 → 通知 watcher
        if (drew > 0)
            EffectRuntime.NotifyWatcher(EffectTrigger.OnDrawCard,
                new Dictionary<string, object?> { ["count"] = drew, ["player"] = playerIdx });
        return drew;
    }

    public static async Task<int> DrawAsync(GameState s, int playerIdx, int n)
    {
        var prompts = EffectRuntime.CurrentPrompts
            ?? throw new InvalidOperationException("交互式抽牌必须在效果结算上下文中执行");
        int drew = await TurnEngine.DrawCardAsync(s, playerIdx, n, prompts);
        if (drew > 0)
            EffectRuntime.NotifyWatcher(EffectTrigger.OnDrawCard,
                new Dictionary<string, object?> { ["count"] = drew, ["player"] = playerIdx });
        return drew;
    }

    public static void DiscardHand(PlayerState p, CardInstance card)
    {
        p.Hand.Remove(card);
        p.Trash.Add(card);
        // 手牌因效果被丢弃 → 派发 watcher（OP14-056 绵津见）；仅效果上下文内有效
        EffectRuntime.NotifyHandDiscarded(p, card);
    }

    /// <summary>把卡组顶部 n 张放入废弃区</summary>
    public static void MillTop(PlayerState p, int n)
    {
        for (int i = 0; i < n && p.Deck.Count > 0; i++)
        {
            var top = p.Deck[0]; p.Deck.RemoveAt(0);
            p.Trash.Add(top);
        }
    }

    // ── 力量修正 ──────────────────────────────────────────────────────────

    public static void AddPowerThisTurn(CardInstance c, int delta)
        => c.PowerModThisTurn += delta;

    public static void AddPowerThisBattle(CardInstance c, int delta)
        => c.PowerModThisBattle += delta;

    /// <summary>仅给指定玩家的领袖增加本次战斗力量，不随当前战斗目标切换到角色。</summary>
    public static void AddLeaderPowerThisBattle(GameState s, int playerIdx, int delta)
        => AddPowerThisBattle(s.Players[playerIdx].Leader, delta);

    public static void AddPowerPersistent(CardInstance c, int delta)
        => c.PowerModPersistent += delta;

    /// <summary>给卡加"直到下个对方结束阶段"持续的力量修正（appliedBy=施加方索引，供 TurnEngine 在对方结束阶段清除）。</summary>
    public static void AddPowerUntilOppEnd(CardInstance c, int delta, int appliedBy)
        => c.PowerModsUntilOppEnd.Add(new CardPowerMod { Delta = delta, AppliedBySide = appliedBy });

    /// <summary>
    /// 给卡牌增加“直到指定玩家的下个回合开始时为止”的力量修正。
    /// 记录服务端权威回合编号，使同一准备阶段的重复入口不会提前清除。
    /// </summary>
    public static void AddPowerUntilNextOwnTurnStart(
        GameState state,
        int ownerSide,
        CardInstance card,
        int delta)
    {
        if (ownerSide is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(ownerSide));

        card.PowerModsUntilNextOwnTurnStart.Add(new PowerModUntilNextOwnTurnStart
        {
            Delta = delta,
            OwnerSide = ownerSide,
            AppliedTurnCount = state.TurnCount,
        });
    }

    /// <summary>将原本力量覆盖为指定值，持续到施加方的下个对方结束阶段。</summary>
    public static void SetOriginalPowerUntilOppEnd(CardInstance c, int value, int appliedBy)
        => c.OriginalPowerOverridesUntilOppEnd.Add(new OriginalPowerOverrideUntilOppEnd
        {
            Value = value,
            AppliedBySide = appliedBy,
        });

    // ── 状态切换 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 统一判断卡牌当前能否转为休息状态。<paramref name="prospectiveOwner"/> 用于尚未进入场上的角色，
    /// 使“以休息状态登场”也按其登场后的当前力量判定【霸王色霸气】。
    /// </summary>
    public static bool CanRestCard(GameState? state, CardInstance c, int? prospectiveOwner = null)
    {
        if (c.HasRestriction(RestrictionKind.CannotBeRested)) return false; // "无法转为休息状态"（瞬时来源）
        // 持续来源（ContinuousEffect.GrantRestriction=CannotBeRested，如 OP11-046/GERMA 光环）同样拦截
        if (state is not null && c.Info.Number == "OP15-024")
        {
            int owner = prospectiveOwner ?? state.SideOf(c);
            int acting = EffectRuntime.CurrentActingSide;
            var sourceKind = EffectRuntime.CurrentSource?.Info.Kind;
            if (owner >= 0
                && state.CurrentTurnPlayer != owner
                && acting == 1 - owner
                && sourceKind is CardKind.Leader or CardKind.Character)
                return false;
        }
        if (state is not null && state.HasContinuousRestriction(c, RestrictionKind.CannotBeRested)) return false;
        if (state is not null && !Game.Hex.HexRules.CanRest(state, c, prospectiveOwner)) return false;
        return true;
    }

    public static bool RestCard(CardInstance c)
    {
        var st = EffectRuntime.CurrentState;
        return RestCardCore(st, c);
    }

    /// <summary>无效果上下文的引擎/调试入口也必须显式携带权威状态，避免绕过全局休息限制。</summary>
    public static bool RestCard(GameState state, CardInstance c, int? prospectiveOwner = null)
        => RestCardCore(state, c, prospectiveOwner);

    private static bool RestCardCore(GameState? state, CardInstance c, int? prospectiveOwner = null)
    {
        if (!CanRestCard(state, c, prospectiveOwner)) return false;
        bool was = c.IsTapped;
        c.IsTapped = true;
        if (!was) // 因效果转为休息状态 → 通知 watcher（reason=effect；攻击/阻挡横置由 BattleEngine 以 attack/block 派发）
            EffectRuntime.NotifyWatcher(EffectTrigger.OnCharRested,
                new Dictionary<string, object?>
                {
                    ["restedCardId"] = c.Id.ToString(),
                    ["owner"] = prospectiveOwner ?? state?.SideOf(c) ?? -1,
                    ["actingSide"] = EffectRuntime.CurrentActingSide,
                    ["reason"] = "effect",
                });
        return true;
    }
    public static void ActivateCard(CardInstance c) { c.IsTapped = false; }

    /// <summary>标记下个重置阶段不会转活跃</summary>
    public static void PreventActivateNextReset(CardInstance c)
        => c.CannotActivateNextReset = true;

    /// <summary>「将我方N张卡牌转为休息状态」成本的可休置项数：活跃的 领袖 + 角色 + 舞台 + 咚!!。
    /// 供发动前的可支付判定（不足 N 则不发动）。</summary>
    public static int RestableCount(GameState state, PlayerState p)
    {
        int n = 0;
        if (!p.Leader.IsTapped && CanRestCard(state, p.Leader)) n++;
        n += p.Characters.Count(c => !c.IsTapped && CanRestCard(state, c));
        if (p.StageCard is not null && !p.StageCard.IsTapped && CanRestCard(state, p.StageCard)) n++;
        if (p.ExtraStageCard is not null && !p.ExtraStageCard.IsTapped && CanRestCard(state, p.ExtraStageCard)) n++;
        n += p.CostArea.Count(d => d.State == DonState.Active);
        return n;
    }

    /// <summary>「将我方 N 张角色转为休息状态」专用支付；只允许选择自己的活跃角色。</summary>
    public static async Task<bool> PromptRestOwnCharacters(EffectContext ctx, int n, string text, bool optional = false)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var candidates = me.Characters
            .Where(card => !card.IsTapped && CanRestCard(ctx.State, card))
            .ToList();
        if (candidates.Count < n) return false;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnActiveCharacter", text,
            candidates.Select(card => card.Id.ToString()).ToList(), optional ? 0 : n, n,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = candidates.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        if (chosen.Count < n) return false;
        var selected = chosen
            .Select(id => candidates.FirstOrDefault(candidate => candidate.Id.ToString() == id))
            .ToArray();
        if (selected.Any(card => card is null || card.IsTapped || !CanRestCard(ctx.State, card))) return false;
        foreach (var card in selected)
            if (!RestCard(card!)) return false;
        return true;
    }

    /// <summary>「将我方N张卡牌转为休息状态」通用支付：弹窗让玩家从活跃的 领袖/角色/舞台/咚!! 中选 N 张休置，
    /// 四类同列展示（卡牌走卡图、咚走 donChoices token）。候选不足 N 或玩家未选满 → 返回 false（不支付）。
    /// 卡牌走 RestCard（含"无法休息"守护），咚直接置为休息状态。</summary>
    public static async Task<bool> PromptRestOwnCards(EffectContext ctx, int n, string text, bool optional = false)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var cardCands = new List<CardInstance>();
        bool CanRest(CardInstance card) => !card.IsTapped && CanRestCard(ctx.State, card);

        if (CanRest(me.Leader)) cardCands.Add(me.Leader);
        cardCands.AddRange(me.Characters.Where(CanRest));
        if (me.StageCard is not null && CanRest(me.StageCard)) cardCands.Add(me.StageCard);
        if (me.ExtraStageCard is not null && CanRest(me.ExtraStageCard)) cardCands.Add(me.ExtraStageCard);
        var activeDon = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (cardCands.Count + activeDon.Count < n) return false;

        var validChoices = cardCands.Select(c => c.Id.ToString())
            .Concat(activeDon.Select(d => d.Id.ToString())).ToList();
        var extra = new Dictionary<string, object?>
        {
            ["donChoices"] = activeDon.Select(d => new { id = d.Id.ToString(), state = "Active" }).ToList(),
        };
        // optional=true：「可以将…休息」式可放弃成本，min=0 给出"跳过"，选不满 n 视为放弃(不支付不发动)
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RestOwnCardsOrDon", text,
            validChoices, optional ? 0 : n, n, extra);
        if (pick.Count < n) return false;
        // 串行房间队列中正常不会发生竞态；仍先整体复核，保证多项成本不会部分支付。
        foreach (var pid in pick)
        {
            var don = activeDon.FirstOrDefault(d => d.Id.ToString() == pid);
            if (don is not null)
            {
                if (don.State != DonState.Active) return false;
                continue;
            }
            var card = cardCands.FirstOrDefault(c => c.Id.ToString() == pid);
            if (card is null || card.IsTapped || !CanRestCard(ctx.State, card)) return false;
        }
        foreach (var pid in pick)
        {
            var don = activeDon.FirstOrDefault(d => d.Id.ToString() == pid);
            if (don is not null) { don.State = DonState.Rest; continue; }
            var card = cardCands.FirstOrDefault(c => c.Id.ToString() == pid);
            if (card is not null && !RestCard(card)) return false;
        }
        return true;
    }

    /// <summary>「将对方最多N张卡牌转为休息状态」效果：让玩家从对方活跃的 领袖/角色/舞台/咚!! 中选最多 N 张休置
    /// （min 0，可不选）。四类同列（卡牌走卡图、咚走 donChoices token）。卡牌走 RestCard，咚直接置休息。</summary>
    public static async Task PromptRestOpponentCards(EffectContext ctx, int n)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var cardCands = new List<CardInstance>();
        if (!opp.Leader.IsTapped && CanRestCard(ctx.State, opp.Leader)) cardCands.Add(opp.Leader);
        cardCands.AddRange(opp.Characters.Where(c => !c.IsTapped && CanRestCard(ctx.State, c)));
        if (opp.StageCard is not null && !opp.StageCard.IsTapped && CanRestCard(ctx.State, opp.StageCard)) cardCands.Add(opp.StageCard);
        if (opp.ExtraStageCard is not null && !opp.ExtraStageCard.IsTapped && CanRestCard(ctx.State, opp.ExtraStageCard)) cardCands.Add(opp.ExtraStageCard);
        var activeDon = opp.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (cardCands.Count + activeDon.Count == 0) return;

        var validChoices = cardCands.Select(c => c.Id.ToString())
            .Concat(activeDon.Select(d => d.Id.ToString())).ToList();
        var extra = new Dictionary<string, object?>
        {
            ["donChoices"] = activeDon.Select(d => new { id = d.Id.ToString(), state = "Active" }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RestOpponentCardsOrDon",
            $"将对方最多 {n} 张卡牌转为休息状态（可选活跃 领袖/角色/舞台/咚!!）",
            validChoices, 0, n, extra);
        foreach (var pid in pick)
        {
            var don = activeDon.FirstOrDefault(d => d.Id.ToString() == pid);
            if (don is not null) { don.State = DonState.Rest; continue; }
            var card = cardCands.FirstOrDefault(c => c.Id.ToString() == pid);
            if (card is not null) RestCard(card);
        }
    }

    // ── KO ────────────────────────────────────────────────────────────────

    public static void KO(GameState s, int ownerIdx, CardInstance card)
    {
        // 旧脚本仍大量调用同步入口；在效果结算上下文中统一转入完整异步 KO 流程，
        // 使 PreKO、他卡守护、离场置换和【KO时】不会被绕过。
        var prompts = EffectRuntime.CurrentPrompts;
        if (prompts is not null && ReferenceEquals(EffectRuntime.CurrentState, s))
        {
            KOByEffectAsync(s, ownerIdx, card, prompts, EffectRuntime.CurrentActingSide)
                .GetAwaiter().GetResult();
            return;
        }

        // 持续"因效果不会被KO"保护（自送废弃/满员牺牲走 KOCard，不经此入口，不受保护）
        if (s.IsKoGuarded(card, "effect")) return;
        if (s.IsLeaveGuarded(card, "effect")) return; // 持续防离场光环（如 EB04-057）
        var owner = s.Players[ownerIdx];
        if (!owner.Characters.Contains(card)
            && !ReferenceEquals(owner.StageCard, card)
            && !ReferenceEquals(owner.ExtraStageCard, card)) return;
        BattleEngine.KOCard(s, ownerIdx, card);
        EffectRuntime.NotifyWatcher(EffectTrigger.OnCharLeaveField,
            new Dictionary<string, object?>
            {
                ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx, ["isKo"] = true,
            });
        // 任意角色被KO（效果）：场上他卡可据此反应（如 EB01-047 拉布）
        EffectRuntime.NotifyWatcher(EffectTrigger.OnAnyCharKOd,
            new Dictionary<string, object?> { ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx, ["reason"] = "effect" });
        // 旧脚本的同步效果 KO 无法在此 await 交互式【KO时】，登记后由 EffectRuntime 在当前效果结束时定向结算。
        s.EnqueueKOEffect(ownerIdx, card, EffectRuntime.CurrentActingSide, EffectRuntime.CurrentSource?.Id);
    }

    /// <summary>
    /// 因效果 KO（异步，带置换守护）：相比同步 KO，额外走 PreKO（受害者自身置换）+ OnAllyWillBeKOd（守护者置换）
    /// + 受害者 OnKO 反应，并设置 KO 来源标记供"因对方的效果而被KO"判定。供 DSL 的 KO op 使用，
    /// 覆盖绝大多数效果KO；脚本直接调用的同步 KO 不享守护（已文档化）。
    /// actingSide=发动本次 KO 效果的一方（用于"对方的效果"判定）。deferOnKO=true 时，
    /// 将受害者【KO时】加入最外层效果结束后的待结算队列，供“先完整处理当前效果，再处理成本卡触发”的卡效使用。
    /// 返回是否实际 KO。
    /// </summary>
    public static async Task<bool> KOByEffectAsync(
        GameState s,
        int ownerIdx,
        CardInstance card,
        IPromptService prompts,
        int actingSide,
        bool deferOnKO = false)
    {
        // 设置 KO 来源（effect + 发起方 + 来源卡），供受害者/守护者判定。
        // 嵌套效果 KO 结束后恢复外层上下文，不能把外层批次误降级成无来源 KO。
        var previousReason = s.KOReason;
        var previousActingSide = s.KOActingSide;
        var previousSource = s.KOSourceCardId;
        s.KOReason = "effect";
        s.KOActingSide = actingSide;
        s.KOSourceCardId = EffectRuntime.CurrentSource?.Id;
        try
        {
            // 战斗 KO、单张效果 KO 与同时效果 KO 共用唯一置换入口，保证顺序、取消与批次覆盖一致。
            if (await BattleEngine.IsKOReplacedAsync(s, ownerIdx, card, prompts)) return false;

            // 实际 KO（复用同步移除逻辑）
            BattleEngine.KOCard(s, ownerIdx, card);
            EffectRuntime.NotifyWatcher(EffectTrigger.OnCharLeaveField,
                new Dictionary<string, object?>
                {
                    ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx, ["isKo"] = true,
                });
            EffectRuntime.NotifyWatcher(EffectTrigger.OnAnyCharKOd,
                new Dictionary<string, object?> { ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx, ["reason"] = "effect" });
            // 受害者 OnKO：卡已进入废弃区，但效果在"原场上位置"发动（如 EB01-057 白星因对方效果被KO）。
            // 个别卡效要求先完整处理当前效果，再处理作为成本被 KO 的角色触发；此时复用既有延迟队列。
            if (deferOnKO)
                s.EnqueueKOEffect(ownerIdx, card, actingSide, EffectRuntime.CurrentSource?.Id);
            else
                await EffectRuntime.Resolve(s, ownerIdx, card, EffectTrigger.OnKO, prompts);
            return true;
        }
        finally
        {
            s.KOReason = previousReason;
            s.KOActingSide = previousActingSide;
            s.KOSourceCardId = previousSource;
        }
    }

    /// <summary>同一个效果同时 KO 多张角色。统一设置 effect 来源上下文，令置换、防离场、【KO时】及
    /// “因对方效果被KO”判定都按效果 KO 处理；所有受害者会在置换检查时保持同时在场。</summary>
    public static async Task<int> KOCardsByEffectAsync(
        GameState s, int ownerIdx, IReadOnlyCollection<CardInstance> cards, IPromptService prompts, int actingSide)
    {
        var previousReason = s.KOReason;
        var previousActingSide = s.KOActingSide;
        var previousSource = s.KOSourceCardId;
        s.KOReason = "effect";
        s.KOActingSide = actingSide;
        s.KOSourceCardId = EffectRuntime.CurrentSource?.Id;
        try
        {
            return await BattleEngine.KOCardsSimultaneouslyAsync(s, ownerIdx, cards, prompts);
        }
        finally
        {
            s.KOReason = previousReason;
            s.KOActingSide = previousActingSide;
            s.KOSourceCardId = previousSource;
        }
    }

    /// <summary>
    /// 效果离场置换守护：某卡因"对方效果"将要离开场上(退手牌/回卡组/置入生命等非KO离场)前调用。
    /// 派发 OnAllyWillLeaveField 给受害卡所属方的守护卡(代替离场效果)；若守护卡 MarkPreventLeave 则取消本次离场。
    /// 返回 true=离场被阻止(调用方应跳过本次离场)。仅在"对方效果"(CurrentActingSide 为受害方对手)时生效。
    /// </summary>
    public static async Task<bool> TryEffectLeaveGuard(GameState s, int victimOwner, CardInstance card, IPromptService prompts, string kind)
    {
        // “此角色将要离开场上”类自身置换不区分效果来源；先于只响应“对方效果”的守护者派发。
        if (EffectRuntime.HasEffectForTrigger(card, EffectTrigger.OnSelfWillLeaveField))
        {
            s.PreventLeaveCardIds.Remove(card.Id);
            await EffectRuntime.Resolve(s, victimOwner, card, EffectTrigger.OnSelfWillLeaveField, prompts,
                new Dictionary<string, object?>
                {
                    ["victimId"] = card.Id.ToString(),
                    ["victimOwner"] = victimOwner,
                    ["kind"] = kind,
                });
            if (s.PreventLeaveCardIds.Remove(card.Id)) return true;
        }

        int acting = EffectRuntime.CurrentActingSide;
        if (acting < 0 || acting == victimOwner) return false; // 非"对方效果"(或无效果上下文)

        // 同一张卡牌效果内已经支付过的离场置换，对之后才确定或逐条处理的匹配目标同样生效。
        // 这既覆盖显式的“同时离场”批次，也兼容旧脚本把一个效果拆成多个离场步骤的情况。
        if (EffectRuntime.IsEffectLeaveReplacementCovered(s, victimOwner, card)) return true;

        // A replacement that has already paid for the current simultaneous leave
        // process covers this victim without prompting or paying a second time.
        if (s.SimultaneousLeaveVictimIds?.Contains(card.Id) == true && s.PreventLeaveCardIds.Remove(card.Id))
            return true;

        var side = s.Players[victimOwner];
        var guardians = new List<CardInstance> { side.Leader };
        guardians.AddRange(side.Characters);
        if (side.StageCard is not null) guardians.Add(side.StageCard);
        if (side.ExtraStageCard is not null) guardians.Add(side.ExtraStageCard);
        s.PreventLeaveCardIds.Remove(card.Id);
        // 不跳过受害卡本身：支持"此角色将要离场时改为…使其不离场"的自我置换
        foreach (var g in guardians.ToList())
        {
            if (!EffectRuntime.HasEffectForTrigger(g, EffectTrigger.OnAllyWillLeaveField)) continue;
            await EffectRuntime.Resolve(s, victimOwner, g, EffectTrigger.OnAllyWillLeaveField, prompts,
                new Dictionary<string, object?> { ["victimId"] = card.Id.ToString(), ["victimOwner"] = victimOwner, ["kind"] = kind });
            // 代替效果可能以当前受害角色自身离场作为成本（例如两张 OP16-014 同时被处理时，
            // 其中一张马尔高 KO 自己来代替整批离场）。此时原离场已被置换，不能继续用
            // 结算前缓存的守护者列表询问下一张同名守护卡。
            if (!side.Characters.Contains(card)) return true;
            if (s.PreventLeaveCardIds.Contains(card.Id)) { s.PreventLeaveCardIds.Remove(card.Id); return true; }
        }
        return false;
    }

    /// <summary>
    /// Resolves a group of characters leaving from the same effect. All replacement
    /// checks happen while the complete group is still on the field, allowing a
    /// single replacement cost to cover every matching character in that process.
    /// </summary>
    public static async Task<int> ProcessEffectLeavesAsync(
        GameState s,
        int victimOwner,
        IReadOnlyCollection<CardInstance> cards,
        IPromptService prompts,
        string kind,
        Action<GameState, int, CardInstance> leave)
    {
        var victims = cards
            .Where(card => s.Players[victimOwner].Characters.Contains(card))
            .DistinctBy(card => card.Id)
            .ToList();
        if (victims.Count == 0) return 0;

        var previousBatch = s.SimultaneousLeaveVictimIds;
        var victimIds = victims.Select(card => card.Id).ToHashSet();
        s.SimultaneousLeaveVictimIds = victimIds;
        foreach (var id in victimIds) s.PreventLeaveCardIds.Remove(id);

        try
        {
            var protectedIds = new HashSet<Guid>();
            foreach (var card in victims)
            {
                if (!s.Players[victimOwner].Characters.Contains(card)) continue;
                if (await TryEffectLeaveGuard(s, victimOwner, card, prompts, kind))
                    protectedIds.Add(card.Id);
            }

            int count = 0;
            foreach (var card in victims)
            {
                if (protectedIds.Contains(card.Id) || !s.Players[victimOwner].Characters.Contains(card)) continue;
                leave(s, victimOwner, card);
                count++;
            }
            return count;
        }
        finally
        {
            foreach (var id in victimIds) s.PreventLeaveCardIds.Remove(id);
            s.SimultaneousLeaveVictimIds = previousBatch;
        }
    }

    // ── 关键字 ────────────────────────────────────────────────────────────

    public static void GiveKeyword(CardInstance c, string keyword, KeywordDuration duration, int appliedBy = -1)
    {
        c.GainedKeywords.Add(new TemporaryKeyword { Keyword = keyword, Duration = duration, AppliedBySide = appliedBy });
    }

    // ── 咚操作 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 让玩家选择 0..max 张当前符合条件的咚，并在响应后重新校验数量后统一应用。
    /// 这是“最多 N 张”效果专用入口；响应无效、取消或可用数量已不足所选值时均不应用，
    /// 避免旧快照导致部分结算。强制处理固定数量的效果不得调用此方法。
    /// </summary>
    public static async Task<int> PromptChooseAndApplyDonCount(
        GameState state,
        IPromptService prompts,
        int playerIdx,
        int max,
        string text,
        Func<DonCard, bool> isEligible,
        Action<DonCard> apply)
    {
        if (max <= 0 || playerIdx < 0 || playerIdx >= state.Players.Length) return 0;

        var player = state.Players[playerIdx];
        int limit = Math.Min(max, player.CostArea.Count(isEligible));
        if (limit <= 0) return 0;

        var options = Enumerable.Range(0, limit + 1)
            .Select(count => $"{count} 张")
            .ToList();
        int chosenCount = await prompts.ChooseOption(playerIdx, text, options);
        if (chosenCount <= 0 || chosenCount > limit) return 0;

        // 提示期间牌局可能被取消、恢复或由测试注入状态变化；只以响应时的权威状态结算。
        var currentEligible = player.CostArea.Where(isEligible).Take(chosenCount).ToList();
        if (currentEligible.Count != chosenCount) return 0;

        foreach (var don in currentEligible) apply(don);
        return chosenCount;
    }

    /// <summary>
    /// 从费用区选 n 张指定状态(fromState)的咚附给 target，返回实际赋予数。
    /// 严格按 fromState 取咚，不做跨状态回退：
    ///   - 「赋予休息状态的咚!!」(fromState=Rest) 只取费用区中已是休息态的咚；无休息咚则不赋予
    ///     （选择后无事发生）。真实对局里此类效果在支付费用横置咚之后结算，必有休息咚；仅 GM
    ///     不付费召唤等场景才会出现 0 休息咚——此时按规范不消耗活跃咚。
    ///   - 「赋予活跃咚」(fromState=Active) 同理只取活跃咚。
    /// 注：历史上曾在 Rest 不足时回退取活跃咚，现按需求改为不回退（见 ST17-004 修复记录）。
    /// </summary>
    public static int AttachDonFromCost(PlayerState p, Guid targetId, int n, DonState fromState = DonState.Active)
    {
        int attached = 0;
        foreach (var d in p.CostArea)
        {
            if (attached >= n) break;
            if (d.State == fromState)
            {
                d.State = DonState.Attached;
                d.AttachedToCardId = targetId;
                attached++;
            }
        }
        return attached;
    }

    /// <summary>从咚!!卡组取 n 张赋予给 target（Attached）；受费用区上限(10)约束。返回实际赋予数。
    /// 引擎 Attached 状态不分横竖，「休息状态的赋予咚」与「活跃赋予咚」力量贡献一致(+1000/张)，
    /// 下个准备阶段会解除赋予→Rest→Active 回到费用区，符合规则。</summary>
    public static int AttachDonFromDeck(PlayerState p, Guid targetId, int n)
    {
        int attached = 0;
        int capacity = Math.Max(10, p.DonDeck.Count + p.CostArea.Count);
        while (attached < n && p.DonDeck.Count > 0 && p.CostArea.Count < capacity)
        {
            var d = p.DonDeck[0];
            p.DonDeck.RemoveAt(0);
            d.State = DonState.Attached;
            d.AttachedToCardId = targetId;
            p.CostArea.Add(d);
            attached++;
        }
        return attached;
    }

    /// <summary>把 n 张咚（按状态）放回咚卡组（实现"咚!!-N"）</summary>
    public static int ReturnDonToDeck(PlayerState p, int n)
    {
        // 优先放回活跃咚，其次休息
        int returned = 0;
        for (int i = p.CostArea.Count - 1; i >= 0 && returned < n; i--)
        {
            var d = p.CostArea[i];
            if (d.State == DonState.Active || d.State == DonState.Rest)
            {
                d.State = DonState.InDeck;
                d.AttachedToCardId = null;
                p.CostArea.RemoveAt(i);
                p.DonDeck.Add(d);
                returned++;
            }
        }
        if (returned > 0) // 咚!!放回咚!!卡组 → 通知 watcher
        {
            var state = EffectRuntime.CurrentState;
            int owner = state is null ? -1
                : ReferenceEquals(state.Players[0], p) ? 0
                : ReferenceEquals(state.Players[1], p) ? 1 : -1;
            EffectRuntime.NotifyWatcher(EffectTrigger.OnDonReturnedToDeck,
                new Dictionary<string, object?> { ["count"] = returned, ["owner"] = owner });
        }
        return returned;
    }

    /// <summary>
    /// 「咚!!-N」通用支付：让玩家从费用区(活跃/休息/附着在角色·领袖身上)手选 N 张咚放回咚!!卡组。
    /// 合格咚 = 费用区全部状态的咚；不足 N → 返回 false(无法支付，调用方应中止发动)；
    /// optional=true 时始终弹出选择，让玩家可以取消发动；optional=false 用于规则要求的强制返还，
    /// 此时若所有合格咚均为活跃状态则直接支付，存在休息/附着咚时仍要求玩家选择具体卡牌。
    /// 玩家取消/超时返回 false。
    /// 放回附着咚会使对应角色/领袖失去贴咚加成(power 由 AttachedDonCount 派生，自动生效)。
    /// </summary>
    public static Task<bool> PromptReturnDonToDeck(EffectContext ctx, int n, bool optional = true)
        => PromptReturnDonToDeck(ctx, ctx.OwnerIndex, n, optional);

    /// <summary>
    /// 原子支付“咚!!-1，并丢弃 1 张手牌”复合成本。
    /// 先按卡文顺序收集咚与手牌选择，两个选择都完整、合法且仍在原区域时才一次性提交；
    /// 取消、超时、重复 id、越权 id 或恢复后状态已变化时均保持零修改。
    /// </summary>
    public static async Task<bool> PromptReturnOneDonAndDiscardOneHand(
        EffectContext ctx,
        Func<CardInstance, bool> discardPredicate)
    {
        var player = ctx.State.Players[ctx.OwnerIndex];
        var eligibleDons = player.CostArea
            .Where(don => don.State is DonState.Active or DonState.Rest or DonState.Attached)
            .ToList();
        var discardCandidates = player.Hand.Where(discardPredicate).ToList();
        if (eligibleDons.Count == 0 || discardCandidates.Count == 0) return false;

        var donAnswer = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReturnOwnDon",
            "选择 1 张咚!!放回咚!!卡组，或取消发动",
            eligibleDons.Select(don => don.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?>
            {
                ["donChoices"] = BuildDonPromptChoices(player, eligibleDons),
                ["canCancel"] = true,
            });
        if (donAnswer.Count != 1) return false;
        var chosenDon = eligibleDons.FirstOrDefault(don => don.Id.ToString() == donAnswer[0]);
        if (chosenDon is null) return false;

        var discardAnswer = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃 1 张手牌（成本）",
            discardCandidates.Select(card => card.Id.ToString()).ToList(), 1, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = discardCandidates
                    .Select(card => new { id = card.Id.ToString(), number = card.Info.Number })
                    .ToList(),
            });
        if (discardAnswer.Count != 1) return false;
        var chosenDiscard = discardCandidates
            .FirstOrDefault(card => card.Id.ToString() == discardAnswer[0]);
        if (chosenDiscard is null) return false;

        // 两次等待期间可能发生取消、恢复或状态重放；提交前以当前权威状态重新验证。
        if (!player.CostArea.Contains(chosenDon)
            || chosenDon.State is not (DonState.Active or DonState.Rest or DonState.Attached)
            || !player.Hand.Contains(chosenDiscard)
            || !discardPredicate(chosenDiscard))
            return false;

        // 完整验证后无 await，按卡文顺序一次性提交，避免只支付一半成本。
        chosenDon.State = DonState.InDeck;
        chosenDon.AttachedToCardId = null;
        player.CostArea.Remove(chosenDon);
        player.DonDeck.Add(chosenDon);

        EffectRuntime.PayingCost = true;
        try { DiscardHand(player, chosenDiscard); }
        finally { EffectRuntime.PayingCost = false; }

        EffectRuntime.NotifyWatcher(EffectTrigger.OnDonReturnedToDeck,
            new Dictionary<string, object?> { ["count"] = 1, ["owner"] = ctx.OwnerIndex });
        return true;
    }

    /// <summary>
    /// 原子支付“将 N 张活跃咚!!转为休息状态，并丢弃 1 张手牌”的复合成本。
    /// 活跃咚没有附着关系，彼此规则等价，因此无需额外选咚；手牌选择完成后重新验证同一批咚与手牌，
    /// 再在无等待窗口内一次提交。取消、超时或状态已变化时保持零修改。
    /// </summary>
    public static async Task<bool> PromptRestActiveDonAndDiscardOneHand(
        EffectContext ctx,
        int donCount,
        Func<CardInstance, bool> discardPredicate)
    {
        if (donCount <= 0) return false;
        var player = ctx.State.Players[ctx.OwnerIndex];
        var activeDons = player.CostArea
            .Where(don => don.State == DonState.Active)
            .Take(donCount)
            .ToList();
        var discardCandidates = player.Hand.Where(discardPredicate).ToList();
        if (activeDons.Count != donCount || discardCandidates.Count == 0) return false;

        var answer = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "选择丢弃 1 张手牌（成本）",
            discardCandidates.Select(card => card.Id.ToString()).ToList(), 1, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = discardCandidates
                    .Select(card => new { id = card.Id.ToString(), number = card.Info.Number })
                    .ToList(),
            });
        if (answer.Count != 1) return false;
        var discard = discardCandidates.FirstOrDefault(card => card.Id.ToString() == answer[0]);
        if (discard is null) return false;

        // 等待选择期间可能发生取消、恢复或状态重放；只接受最初选定资源仍全部有效的情况。
        if (activeDons.Any(don => !player.CostArea.Contains(don) || don.State != DonState.Active)
            || !player.Hand.Contains(discard)
            || !discardPredicate(discard))
            return false;

        foreach (var don in activeDons) don.State = DonState.Rest;
        EffectRuntime.PayingCost = true;
        try { DiscardHand(player, discard); }
        finally { EffectRuntime.PayingCost = false; }
        return true;
    }

    /// <summary>构造咚选择项，并补充附着目标实例、卡号与卡名，供客户端明确区分领袖和同名角色。</summary>
    private static List<object> BuildDonPromptChoices(PlayerState player, IEnumerable<DonCard> dons)
    {
        return dons.Select(don =>
        {
            string? attachedToCardId = null;
            CardInstance? attachedTarget = null;
            if (don.State == DonState.Attached && don.AttachedToCardId is { } targetId)
            {
                attachedToCardId = targetId.ToString();
                attachedTarget = player.Leader.Id == targetId
                    ? player.Leader
                    : player.Characters.FirstOrDefault(card => card.Id == targetId);
            }

            return (object)new
            {
                id = don.Id.ToString(),
                state = don.State.ToString(),
                attachedToCardId,
                attachedToNumber = attachedTarget?.Info.Number,
                attachedToName = attachedTarget?.Info.Name,
            };
        }).ToList();
    }

    /// <summary>“将 1 张或更多咚!!放回咚!!卡组”成本：玩家可选择费用区任意正数张咚，0 张视为放弃。</summary>
    public static async Task<bool> PromptReturnAtLeastOneDonToDeck(EffectContext ctx)
    {
        var player = ctx.State.Players[ctx.OwnerIndex];
        var eligible = player.CostArea
            .Where(don => don.State is DonState.Active or DonState.Rest or DonState.Attached)
            .ToList();
        if (eligible.Count == 0) return false;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReturnOwnDon",
            "选择 1 张或更多咚!!放回咚!!卡组，或取消发动",
            eligible.Select(don => don.Id.ToString()).ToList(), 0, eligible.Count,
            new Dictionary<string, object?>
            {
                ["donChoices"] = BuildDonPromptChoices(player, eligible),
                ["canCancel"] = true,
                ["allowVariableReturnCount"] = true,
            });
        if (chosen.Count == 0 || chosen.Count != chosen.Distinct(StringComparer.Ordinal).Count()) return false;
        var selected = chosen
            .Select(id => eligible.FirstOrDefault(item => item.Id.ToString() == id))
            .ToList();
        if (selected.Any(don => don is null
                || !player.CostArea.Contains(don)
                || don.State is not (DonState.Active or DonState.Rest or DonState.Attached)))
            return false;

        // 完整复验后无 await，一次提交全部选择，避免恢复/重放导致部分返还。
        foreach (var don in selected.OfType<DonCard>())
        {
            player.CostArea.Remove(don);
            don.State = DonState.InDeck;
            don.AttachedToCardId = null;
            player.DonDeck.Add(don);
        }
        EffectRuntime.NotifyWatcher(EffectTrigger.OnDonReturnedToDeck,
            new Dictionary<string, object?> { ["count"] = selected.Count, ["owner"] = ctx.OwnerIndex });
        return true;
    }

    /// <summary>
    /// 让指定玩家从其场上选择咚!!放回咚!!卡组。除支付己方“咚!!-N”外，也用于
    /// “对方将其场上的咚!!放回咚!!卡组”这类强制效果。
    /// </summary>
    public static async Task<bool> PromptReturnDonToDeck(
        EffectContext ctx,
        int playerIdx,
        int n,
        bool optional,
        bool requireExplicitChoice = false)
    {
        if (n <= 0) return true;
        var player = ctx.State.Players[playerIdx];
        // 合格咚 = 费用区全部(活跃 + 休息 + 附着)
        var eligible = player.CostArea
            .Where(d => d.State is DonState.Active or DonState.Rest or DonState.Attached)
            .ToList();
        if (eligible.Count < n) return false;   // 凑不够 → 无法支付

        List<DonCard> chosen;
        // 「咚!!-N」成本均可选择是否发动：即使全为活跃咚，也必须给玩家取消机会。
        // “由某玩家将其咚返回”需显式选择时，由调用方开启 requireExplicitChoice；
        // 这也覆盖全为活跃咚但实例级标记不同、选择哪张仍会影响后续局面的情况。
        bool needPrompt = optional
            || requireExplicitChoice
            || eligible.Any(d => d.State != DonState.Active);
        if (!needPrompt)
        {
            chosen = eligible.Take(n).ToList();
        }
        else
        {
            var donChoices = BuildDonPromptChoices(player, eligible);

            var ans = await ctx.Prompts.ChooseCards(playerIdx, "ReturnOwnDon",
                optional
                    ? $"选择 {n} 张咚!! 放回咚!!卡组，或取消发动"
                    : $"选择 {n} 张咚!! 放回咚!!卡组",
                eligible.Select(d => d.Id.ToString()).ToList(), optional ? 0 : n, n,
                new Dictionary<string, object?>
                {
                    ["donChoices"] = donChoices,
                    ["canCancel"] = optional,
                });

            // 固定成本必须恰好选择 N 个不同实例。重复、超量、缺失或未知 ID 均整笔拒绝，
            // 不能截断为前 N 个后继续支付。
            if (ans.Count != n || ans.Distinct(StringComparer.Ordinal).Count() != n) return false;
            chosen = ans
                .Select(id => eligible.FirstOrDefault(don => don.Id.ToString() == id))
                .OfType<DonCard>()
                .ToList();
            if (chosen.Count != n) return false;
        }

        // Prompt await、恢复或测试注入都可能改变权威状态；提交前必须验证仍是同一批费用区实例
        // 且状态仍允许支付。先完整验证、后无 await 一次提交，避免部分放回。
        if (chosen.Any(don => !player.CostArea.Contains(don)
                || don.State is not (DonState.Active or DonState.Rest or DonState.Attached)))
            return false;
        foreach (var d in chosen)
        {
            d.State = DonState.InDeck;
            d.AttachedToCardId = null;
            player.CostArea.Remove(d);
            player.DonDeck.Add(d);
        }
        EffectRuntime.NotifyWatcher(EffectTrigger.OnDonReturnedToDeck,
            new Dictionary<string, object?> { ["count"] = chosen.Count, ["owner"] = playerIdx });
        return true;
    }

    // ── 移动卡牌 ──────────────────────────────────────────────────────────

    /// <summary>把场上一张卡放回手牌</summary>
    public static void BounceToHand(GameState s, int ownerIdx, CardInstance card)
    {
        if (s.IsLeaveGuarded(card, "effect")) return; // 持续防离场光环（如 EB04-057）
        var p = s.Players[ownerIdx];
        bool removed = p.Characters.Remove(card);
        if (ReferenceEquals(p.StageCard, card))
        {
            p.StageCard = null;
            removed = true;
        }
        if (ReferenceEquals(p.ExtraStageCard, card))
        {
            p.ExtraStageCard = null;
            removed = true;
        }
        // 重复、乱序或错误持有者请求不得把同一实例再次加入手牌。
        if (!removed) return;
        // 归还附着咚
        foreach (var d in p.CostArea)
        {
            if (d.State == DonState.Attached && d.AttachedToCardId == card.Id)
            {
                d.State = DonState.Rest;
                d.AttachedToCardId = null;
            }
        }
        ResetCardEphemeralState(card);
        p.Hand.Add(card);
        EffectRuntime.NotifyWatcher(EffectTrigger.OnCharLeaveField,
            new Dictionary<string, object?> { ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx });
    }

    /// <summary>把场上一张角色/舞台放置到废弃区（非 KO，不触发【KO时】）。</summary>
    public static void TrashFieldCard(
        GameState s,
        int ownerIdx,
        CardInstance card,
        bool ignoreEffectLeaveGuard = false)
    {
        // “不会因对方的效果离场”不能阻止玩家把自己的角色作为成本放入废弃区。
        // 默认仍尊重效果离场保护；只有明确的成本调用方可以选择绕过。
        if (!ignoreEffectLeaveGuard && s.IsLeaveGuarded(card, "effect")) return;
        var p = s.Players[ownerIdx];
        bool removed = p.Characters.Remove(card);
        if (ReferenceEquals(p.StageCard, card))
        {
            p.StageCard = null;
            removed = true;
        }
        if (ReferenceEquals(p.ExtraStageCard, card))
        {
            p.ExtraStageCard = null;
            removed = true;
        }
        if (!removed) return;
        foreach (var don in p.CostArea)
        {
            if (don.State == DonState.Attached && don.AttachedToCardId == card.Id)
            {
                don.State = DonState.Rest;
                don.AttachedToCardId = null;
            }
        }
        ResetCardEphemeralState(card);
        p.Trash.Add(card);
        EffectRuntime.NotifyWatcher(EffectTrigger.OnCharLeaveField,
            new Dictionary<string, object?> { ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx });
    }

    /// <summary>把手牌中的角色卡免费登场</summary>
    /// <summary>角色区满员（5张）时为效果登场腾位：有效果 Prompt 上下文则让登场方自选 1 张角色送废弃区
    /// （与正常出牌的 OverflowTrash 流程一致，非 KO、不触发【K.O.时】）；无上下文时回退旧行为（挤最左）。
    /// 供 PlayFromHandFree / PlayFromTrashFree / PlayFromDeckFree / PlayFromLifeFree 及脚本卡满场登场复用。</summary>
    public static async Task SqueezeCharacterSlot(GameState s, int playerIdx)
    {
        var p = s.Players[playerIdx];
        if (p.Characters.Count < 5) return;
        var victim = p.Characters[0];
        var prompts = EffectRuntime.CurrentPrompts;
        if (prompts is not null && ReferenceEquals(EffectRuntime.CurrentState, s))
        {
            try
            {
                var picked = await prompts.ChooseCards(playerIdx, "OverflowTrash",
                    "角色区已满，请选择 1 张角色送去废弃区",
                    p.Characters.Select(c => c.Id.ToString()).ToList(), 1, 1);
                if (picked.Count > 0)
                    victim = p.Characters.FirstOrDefault(c => c.Id.ToString() == picked[0]) ?? victim;
            }
            catch { /* Prompt 异常时回退挤最左，避免卡死效果链 */ }
        }
        p.Characters.Remove(victim);
        p.Trash.Add(victim);
    }

    /// <summary>
    /// 把舞台放入合法舞台槽。拥有“三号船坞”时保留两个槽；两个槽都占用时，
    /// 在效果 Prompt 上下文中由玩家选择废弃哪张，恢复/无交互路径固定废弃主槽，保证重放确定性。
    /// 调用方负责先从原区域移除新舞台，并在放置后登记登场效果。
    /// </summary>
    private static async Task PlaceStageAsync(GameState s, int playerIdx, CardInstance card)
    {
        var player = s.Players[playerIdx];
        if (!Game.Hex.HexRules.HasLegacyDockSlots(s, playerIdx))
        {
            if (player.StageCard is not null) player.Trash.Add(player.StageCard);
            player.StageCard = card;
            return;
        }

        if (player.StageCard is null)
        {
            player.StageCard = card;
            return;
        }
        if (player.ExtraStageCard is null)
        {
            player.ExtraStageCard = card;
            return;
        }

        var stages = new[] { player.StageCard, player.ExtraStageCard };
        var victim = player.StageCard;
        var prompts = EffectRuntime.CurrentPrompts;
        if (prompts is not null && ReferenceEquals(EffectRuntime.CurrentState, s))
        {
            try
            {
                var picked = await prompts.ChooseCards(
                    playerIdx,
                    "HexStageOverflowTrash",
                    "三号船坞：选择1张现有舞台废弃，再登场新舞台",
                    stages.Select(stage => stage.Id.ToString()).ToList(),
                    1,
                    1,
                    new Dictionary<string, object?>
                    {
                        ["choiceCards"] = stages.Select(stage => new
                        {
                            id = stage.Id.ToString(),
                            number = stage.Info.Number,
                        }).ToArray(),
                    });
                victim = stages.FirstOrDefault(stage => picked.Contains(stage.Id.ToString())) ?? victim;
            }
            catch
            {
                // Prompt 恢复失败时采用固定主槽，不引入未记录的随机分支。
            }
        }

        if (ReferenceEquals(player.StageCard, victim)) player.StageCard = card;
        else player.ExtraStageCard = card;
        player.Trash.Add(victim);
    }

    public static async Task PlayFromHandFree(GameState s, int playerIdx, CardInstance card)
    {
        var p = s.Players[playerIdx];
        if (IsCharacterPlayRestricted(s, playerIdx, card)) return;
        // OP12-036：该能力只限制“手牌中的此卡牌”被效果登场，正常支付费用登场不受影响。
        if (card.Info.Abilities.Contains("无法通过效果登场")) return;
        if (!p.Hand.Remove(card)) return;
        if (card.Info.Kind == CardKind.Character)
        {
            if (p.Characters.Count >= 5)
                await SqueezeCharacterSlot(s, playerIdx);
            card.TurnPlayed = s.TurnCount;
            card.IsTapped = s.ShouldCharacterEnterRested(playerIdx, card);
            p.Characters.Add(card);
            s.EnqueueEnterField(playerIdx, card, "hand"); // 触发被登场角色的【登场时】
        }
        else if (card.Info.Kind == CardKind.Stage)
        {
            await PlaceStageAsync(s, playerIdx, card);
            s.EnqueueEnterField(playerIdx, card, "hand");
        }
        // 事件类暂不在此入口处理
    }

    /// <summary>把手牌中的事件卡免费发动（效果走 EffectRuntime）</summary>
    public static void PlayEventFromHandFree(GameState s, int playerIdx, CardInstance card, IPromptService prompts)
    {
        var p = s.Players[playerIdx];
        if (!p.Hand.Remove(card)) return;
        p.Trash.Add(card);
        EffectRuntime.Resolve(s, playerIdx, card, EffectTrigger.EventMain, prompts).GetAwaiter().GetResult();
    }

    // ── 查询 ───────────────────────────────────────────────────────────────

    public static int CountTrashByFilter(PlayerState p, Func<CardInstance, bool> filter)
        => p.Trash.Count(filter);

    public static IReadOnlyList<CardInstance> RevealTopK(PlayerState p, int k)
        => p.Deck.Take(k).ToList();

    /// <summary>把 top k 张中指定的一张加入手牌，其余按 chosenOrder 放回卡组底部</summary>
    public static void RevealPickAndBottom(PlayerState p, int k, int pickIndex)
    {
        if (k <= 0 || p.Deck.Count == 0) return;
        var top = p.Deck.Take(k).ToList();
        for (int i = 0; i < top.Count; i++) p.Deck.RemoveAt(0);
        if (pickIndex >= 0 && pickIndex < top.Count)
        {
            var picked = top[pickIndex];
            p.Hand.Add(picked);
            top.RemoveAt(pickIndex);
        }
        // 剩余的放卡组底部（顺序由调用方控制，目前简化为原顺序）
        p.Deck.AddRange(top);
    }

    // ─── A 阶段 P0 新增原子 ────────────────────────────────────────────────

    /// <summary>从咚!!卡组追加 N 张咚到费用区，活跃或休息状态</summary>
    public static int RefreshDonFromDeck(PlayerState p, int n, DonState state = DonState.Active)
    {
        int added = 0;
        int capacity = Math.Max(10, p.DonDeck.Count + p.CostArea.Count);
        for (int i = 0; i < n && p.DonDeck.Count > 0 && p.CostArea.Count < capacity; i++)
        {
            var d = p.DonDeck[0]; p.DonDeck.RemoveAt(0);
            d.State = state;
            d.AttachedToCardId = null;
            p.CostArea.Add(d);
            added++;
        }
        return added;
    }

    /// <summary>把场上的卡放回持有者的卡组最下方（先归还附着咚 + 清临时状态）</summary>
    public static void ReturnFieldToDeckBottom(GameState s, int ownerIdx, CardInstance card)
    {
        if (s.IsLeaveGuarded(card, "effect")) return; // 持续防离场光环（如 EB04-057）
        var p = s.Players[ownerIdx];
        foreach (var d in p.CostArea)
        {
            if (d.State == DonState.Attached && d.AttachedToCardId == card.Id)
            {
                d.State = DonState.Rest;
                d.AttachedToCardId = null;
            }
        }
        p.Characters.Remove(card);
        if (p.StageCard == card) p.StageCard = null;
        if (p.ExtraStageCard == card) p.ExtraStageCard = null;
        ResetCardEphemeralState(card);
        p.Deck.Add(card);
        EffectRuntime.NotifyWatcher(EffectTrigger.OnCharLeaveField,
            new Dictionary<string, object?> { ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx });
    }

    /// <summary>把手牌的卡放回卡组最下方</summary>
    public static void ReturnHandToDeckBottom(PlayerState p, CardInstance card)
    {
        if (!p.Hand.Remove(card)) return;
        ResetCardEphemeralState(card);
        p.Deck.Add(card);
    }

    /// <summary>把废弃区的卡放回卡组最下方</summary>
    public static void ReturnTrashToDeckBottom(PlayerState p, CardInstance card)
    {
        if (!p.Trash.Remove(card)) return;
        ResetCardEphemeralState(card);
        p.Deck.Add(card);
    }

    /// <summary>从废弃区免费登场（restState=true 时以休息状态登场）</summary>
    public static async Task PlayFromTrashFree(
        GameState s,
        int playerIdx,
        CardInstance card,
        bool restState = false,
        bool lifeTriggerOrigin = false)
    {
        var p = s.Players[playerIdx];
        if (IsCharacterPlayRestricted(s, playerIdx, card)) return;
        if (!p.Trash.Remove(card)) return;
        if (card.Info.Kind == CardKind.Character)
        {
            if (p.Characters.Count >= 5)
                await SqueezeCharacterSlot(s, playerIdx);
            ResetCardEphemeralState(card);
            card.TurnPlayed = s.TurnCount;
            card.IsTapped = (restState || s.ShouldCharacterEnterRested(playerIdx, card))
                && CanRestCard(s, card, playerIdx);
            p.Characters.Add(card);
            s.EnqueueEnterField(playerIdx, card, "trash", lifeTriggerOrigin); // 触发被登场角色的【登场时】
        }
        else if (card.Info.Kind == CardKind.Stage)
        {
            ResetCardEphemeralState(card);
            await PlaceStageAsync(s, playerIdx, card);
            s.EnqueueEnterField(playerIdx, card, "trash", lifeTriggerOrigin);
        }
    }

    /// <summary>把废弃区的卡加入手牌</summary>
    public static void TrashToHand(PlayerState p, CardInstance card)
    {
        if (!p.Trash.Remove(card)) return;
        ResetCardEphemeralState(card);
        p.Hand.Add(card);
    }

    /// <summary>把力量本回合"变为"绝对值（不是 ±delta）。实现方式：相对当前总力量算出 delta，写 PowerModThisTurn</summary>
    public static void SetPowerThisTurn(CardInstance c, int absoluteValue, int donAttached, bool ownerTurn)
    {
        int current = c.CurrentPower(donAttached, ownerTurn);
        c.PowerModThisTurn += absoluteValue - current;
    }

    /// <summary>让对手丢弃 N 张手牌（由对手自己 Prompt 选择）。0 张直接返回</summary>
    public static Task OpponentDiscardChosen(GameEngine engine, int opponentIdx, int n)
        => OpponentDiscardChosen(engine.State, engine.Prompts, opponentIdx, n);

    /// <summary>让对手丢弃 N 张手牌（由对手自己选择），可在脚本和测试中直接使用。</summary>
    public static async Task OpponentDiscardChosen(GameState state, IPromptService prompts, int opponentIdx, int n)
    {
        var opp = state.Players[opponentIdx];
        int actual = Math.Min(n, opp.Hand.Count);
        if (actual <= 0) return;
        var chosen = await prompts.ChooseCards(opponentIdx, "OwnHandDiscard",
            $"丢弃 {actual} 张手牌",
            opp.Hand.Select(c => c.Id.ToString()).ToList(), actual, actual);
        if (chosen.Count == 0)
        {
            // 超时未选 → 自动从头丢
            for (int i = 0; i < actual; i++)
                if (opp.Hand.Count > 0) DiscardHand(opp, opp.Hand[0]);
            return;
        }
        foreach (var cid in chosen)
        {
            var card = opp.Hand.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) DiscardHand(opp, card);
        }
    }

    /// <summary>让对手随机丢弃 N 张手牌（对应"丢弃对方N张手牌"措辞——无人选择，随机弃）。
    /// 用确定性 GameState.Rng（与洗牌同源）保证回放/重连一致。区别于 OpponentDiscardChosen("对方丢弃N张"=对方自选)。</summary>
    public static void OpponentDiscardRandom(GameEngine engine, int opponentIdx, int n)
    {
        var opp = engine.State.Players[opponentIdx];
        int actual = Math.Min(n, opp.Hand.Count);
        for (int i = 0; i < actual && opp.Hand.Count > 0; i++)
        {
            int idx = engine.State.Rng.Next(opp.Hand.Count);
            DiscardHand(opp, opp.Hand[idx]);
        }
    }

    /// <summary>清除卡的临时状态（区域间移动时调用）</summary>
    private static void ResetCardEphemeralState(CardInstance c)
    {
        c.IsTapped = false;
        c.PowerModThisTurn = 0;
        c.PowerModThisBattle = 0;
        c.PowerModPersistent = 0;
        c.PowerModsUntilOppEnd.Clear();
        c.PowerModsUntilNextOwnTurnStart.Clear();
        c.OriginalPowerOverridesUntilOppEnd.Clear();
        c.GainedKeywords.Clear();
        c.CannotActivateNextReset = false;
        c.OncePerTurnUsedKeys.Clear();
        c.TurnPlayed = 0;
        c.CostModThisTurn = 0;
        c.CostModPersistent = 0;
        c.CostModsUntilOppEnd.Clear();
        c.OriginalPowerOverride = null;
        c.IsEffectsNullified = false;
        c.Restrictions.Clear();
        c.IsLifeFaceUp = false;
        c.NoAttackCostLeThisTurn = 0;
        c.BattledOpponentCharacterThisTurn = false;
        c.NameAliases.Clear();
        c.GainedPropertiesThisTurn.Clear();
    }

    // ── M2 生命牌正反朝向 ──────────────────────────────────────────────
    /// <summary>将我方生命区最上方 1 张翻至正面朝上（已正面则无变化）。生命区空则无操作。</summary>
    public static void FlipTopLifeFaceUp(PlayerState p)
    {
        if (p.LifeArea.Count > 0) p.LifeArea[0].IsLifeFaceUp = true;
    }

    /// <summary>将我方所有生命卡牌翻至正面朝下。</summary>
    public static void FlipAllLifeFaceDown(PlayerState p)
    {
        foreach (var c in p.LifeArea) c.IsLifeFaceUp = false;
    }

    /// <summary>按给定 Guid 顺序（顶→底）重排某玩家的生命区；未列出的卡按原序补到末尾。</summary>
    public static void ReorderLife(PlayerState p, IReadOnlyList<Guid> order)
    {
        var lookup = p.LifeArea.ToDictionary(c => c.Id, c => c);
        var reordered = order.Where(lookup.ContainsKey).Select(g => lookup[g]).ToList();
        foreach (var c in p.LifeArea) if (!reordered.Contains(c)) reordered.Add(c);
        p.LifeArea.Clear();
        p.LifeArea.AddRange(reordered);
    }

    // ─── B 阶段 P1 新增原子 ────────────────────────────────────────────────

    /// <summary>范围 buff：对某方场上所有符合 filter 的卡，加本回合力量 delta</summary>
    public static int AddPowerToAllThisTurn(GameState s, int sideIdx, Func<CardInstance, bool> filter, int delta, bool includeLeader = true)
    {
        var p = s.Players[sideIdx];
        int affected = 0;
        if (includeLeader && filter(p.Leader)) { p.Leader.PowerModThisTurn += delta; affected++; }
        foreach (var c in p.Characters)
            if (filter(c)) { c.PowerModThisTurn += delta; affected++; }
        return affected;
    }

    /// <summary>主动从卡组顶部加 n 张到生命区最上方</summary>
    public static int AddLifeFromDeckTop(PlayerState p, int n)
    {
        int added = 0;
        for (int i = 0; i < n && p.Deck.Count > 0; i++)
        {
            var top = p.Deck[0]; p.Deck.RemoveAt(0);
            p.LifeArea.Insert(0, top);
            added++;
        }
        return added;
    }

    /// <summary>把场上一张角色卡放到生命区（最上方）：归还附着咚 + 清临时态 + 入生命区</summary>
    public static void MoveCharToLife(GameState s, int ownerIdx, CardInstance card, bool toTop = true)
    {
        if (s.IsLeaveGuarded(card, "effect")) return; // 持续防离场光环（如 EB04-057）
        var p = s.Players[ownerIdx];
        foreach (var d in p.CostArea)
        {
            if (d.State == DonState.Attached && d.AttachedToCardId == card.Id)
            {
                d.State = DonState.Rest;
                d.AttachedToCardId = null;
            }
        }
        p.Characters.Remove(card);
        if (p.StageCard == card) p.StageCard = null;
        if (p.ExtraStageCard == card) p.ExtraStageCard = null;
        ResetCardEphemeralState(card);
        if (toTop) p.LifeArea.Insert(0, card);
        else       p.LifeArea.Add(card);
        EffectRuntime.NotifyWatcher(EffectTrigger.OnCharLeaveField,
            new Dictionary<string, object?> { ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx });
    }

    /// <summary>把卡组中的一张角色/舞台卡免费登场（登场后触发其【登场时】，见 EnqueueEnterField）。调用方负责洗牌。</summary>
    public static async Task PlayFromDeckFree(GameState s, int playerIdx, CardInstance card, bool restState = false)
    {
        var p = s.Players[playerIdx];
        if (IsCharacterPlayRestricted(s, playerIdx, card)) return;
        if (!p.Deck.Remove(card)) return;
        if (card.Info.Kind == CardKind.Character)
        {
            if (p.Characters.Count >= 5)
                await SqueezeCharacterSlot(s, playerIdx);
            ResetCardEphemeralState(card);
            card.TurnPlayed = s.TurnCount;
            card.IsTapped = (restState || s.ShouldCharacterEnterRested(playerIdx, card))
                && CanRestCard(s, card, playerIdx);
            p.Characters.Add(card);
            s.EnqueueEnterField(playerIdx, card, "deck"); // 触发被登场角色的【登场时】
        }
        else if (card.Info.Kind == CardKind.Stage)
        {
            ResetCardEphemeralState(card);
            await PlaceStageAsync(s, playerIdx, card);
            s.EnqueueEnterField(playerIdx, card, "deck");
        }
    }

    /// <summary>把生命区中的一张角色卡免费登场（登场后触发其【登场时】，见 EnqueueEnterField）。</summary>
    public static async Task PlayFromLifeFree(GameState s, int playerIdx, CardInstance card, bool restState = false)
    {
        var p = s.Players[playerIdx];
        if (IsCharacterPlayRestricted(s, playerIdx, card)) return;
        if (!p.LifeArea.Remove(card)) return;
        if (card.Info.Kind == CardKind.Character)
        {
            if (p.Characters.Count >= 5)
                await SqueezeCharacterSlot(s, playerIdx);
            ResetCardEphemeralState(card);
            card.TurnPlayed = s.TurnCount;
            card.IsTapped = (restState || s.ShouldCharacterEnterRested(playerIdx, card))
                && CanRestCard(s, card, playerIdx);
            p.Characters.Add(card);
            s.EnqueueEnterField(playerIdx, card, "life"); // 触发被登场角色的【登场时】
        }
        else
        {
            // 非角色卡无法登场到角色区：退回生命底，避免丢失
            p.LifeArea.Add(card);
        }
    }

    private static bool IsCharacterPlayRestricted(GameState s, int playerIdx, CardInstance card)
        => card.Info.Kind == CardKind.Character
           && (s.NoPlayCharacterThisTurn.Contains(playerIdx)
               || (s.NoPlayCharacterOriginalCostGteThisTurn.TryGetValue(playerIdx, out int blockedCost)
                   && card.Info.Cost >= blockedCost));

    /// <summary>把手牌中的一张卡置入生命区（toTop=true 顶部，faceUp 指定正反朝向）。</summary>
    public static void HandToLife(PlayerState p, CardInstance card, bool toTop = true, bool faceUp = false)
    {
        if (!p.Hand.Remove(card)) return;
        ResetCardEphemeralState(card);
        card.IsLifeFaceUp = faceUp;
        if (toTop) p.LifeArea.Insert(0, card);
        else       p.LifeArea.Add(card);
    }

    /// <summary>看卡组顶 k 张并自由排序放回（顶或底）。order = 重新组合后的卡 Id 顺序</summary>
    public static void ReorderTopK(PlayerState p, IReadOnlyList<Guid> order, bool toBottom)
    {
        var ids = new HashSet<Guid>(order);
        var lookup = p.Deck.Take(ids.Count).ToDictionary(c => c.Id, c => c);
        if (lookup.Count == 0) return;
        for (int i = 0; i < lookup.Count; i++) p.Deck.RemoveAt(0);
        var reordered = order.Where(g => lookup.ContainsKey(g)).Select(g => lookup[g]).ToList();
        if (toBottom) p.Deck.AddRange(reordered);
        else
        {
            for (int i = reordered.Count - 1; i >= 0; i--) p.Deck.Insert(0, reordered[i]);
        }
    }

    /// <summary>检索卡组：按 filter 取所有符合的卡，让玩家选 1 张加入手牌，洗牌</summary>
    public static async Task<CardInstance?> SearchDeck(GameEngine engine, int playerIdx, Func<CardInstance, bool> filter, string prompt = "从卡组选 1 张加入手牌")
    {
        var p = engine.State.Players[playerIdx];
        var candidates = p.Deck.Where(filter).ToList();
        if (candidates.Count == 0)
        {
            engine.ShuffleDeck(p, playerIdx, "search_deck_no_candidate");
            return null;
        }
        var chosen = await engine.Prompts.ChooseCards(playerIdx, "SearchDeck", prompt,
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        CardInstance? picked = null;
        if (chosen.Count > 0)
        {
            picked = candidates.First(c => c.Id.ToString() == chosen[0]);
            p.Deck.Remove(picked);
            p.Hand.Add(picked);
        }
        engine.ShuffleDeck(p, playerIdx, "search_deck");
        // “从卡组检索并加入手牌”按规则公开被检索到的牌；统一走公开广播，
        // 同时让双方的右侧操作日志明确记录具体卡牌。
        if (picked is not null)
            engine.BroadcastReveal(playerIdx, new[] { picked.Info.Number });
        return picked;
    }

    /// <summary>给卡加费用修正；跨回合修正记录施加方，供 TurnEngine 在对应对方结束阶段精确清除。</summary>
    public static void AddCostModifier(CardInstance c, int delta, KeywordDuration duration, int appliedBy = -1)
    {
        if (duration == KeywordDuration.UntilNextOpponentEndPhase)
        {
            c.CostModPersistent += delta;
            c.CostModsUntilOppEnd.Add(new CardCostMod { Delta = delta, AppliedBySide = appliedBy });
        }
        else
        {
            c.CostModThisTurn += delta;
        }
    }

    /// <summary>无效化一张卡的所有效果</summary>
    public static void NullifyEffects(CardInstance c, KeywordDuration duration)
    {
        c.IsEffectsNullified = true;
        // 简化：不区分 duration（统一在 EnterEndPhase 清理）
        _ = duration;
    }

    /// <summary>给卡加限制（CannotAttack 等）</summary>
    public static void AddRestriction(CardInstance c, RestrictionKind kind, KeywordDuration duration, int appliedBy = -1)
    {
        c.Restrictions.Add(new CardRestriction { Kind = kind, Duration = duration, AppliedBySide = appliedBy });
    }

    /// <summary>
    /// 洗牌（Fisher–Yates）。必须传入本局 GameState，使用其确定性 RNG（GameState.Rng）。
    /// 严禁用共享静态 Random：那会让重放无法重现、并发房间互相干扰。
    /// </summary>
    public static void Shuffle<T>(GameState s, List<T> list)
    {
        var rng = s.Rng;
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
