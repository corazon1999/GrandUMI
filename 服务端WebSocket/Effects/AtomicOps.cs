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

    public static void DiscardHand(PlayerState p, CardInstance card)
    {
        p.Hand.Remove(card);
        p.Trash.Add(card);
        // 手牌因效果被丢弃 → 派发 watcher（OP14-056 绵津见）；仅效果上下文内有效
        EffectRuntime.NotifyHandDiscarded(p);
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

    public static void AddPowerPersistent(CardInstance c, int delta)
        => c.PowerModPersistent += delta;

    // ── 状态切换 ──────────────────────────────────────────────────────────

    public static void RestCard(CardInstance c)
    {
        if (c.HasRestriction(RestrictionKind.CannotBeRested)) return; // "无法转为休息状态"（瞬时来源）
        // 持续来源（ContinuousEffect.GrantRestriction=CannotBeRested，如 OP11-046/GERMA 光环）同样拦截
        var st = EffectRuntime.CurrentState;
        if (st is not null && st.HasContinuousRestriction(c, RestrictionKind.CannotBeRested)) return;
        bool was = c.IsTapped;
        c.IsTapped = true;
        if (!was) // 因效果转为休息状态 → 通知 watcher
            EffectRuntime.NotifyWatcher(EffectTrigger.OnCharRested,
                new Dictionary<string, object?> { ["restedCardId"] = c.Id.ToString() });
    }
    public static void ActivateCard(CardInstance c) { c.IsTapped = false; }

    /// <summary>标记下个重置阶段不会转活跃</summary>
    public static void PreventActivateNextReset(CardInstance c)
        => c.CannotActivateNextReset = true;

    /// <summary>「将我方N张卡牌转为休息状态」成本的可休置项数：活跃的 领袖 + 角色 + 舞台 + 咚!!。
    /// 供发动前的可支付判定（不足 N 则不发动）。</summary>
    public static int RestableCount(PlayerState p)
    {
        int n = 0;
        if (!p.Leader.IsTapped) n++;
        n += p.Characters.Count(c => !c.IsTapped);
        if (p.StageCard is not null && !p.StageCard.IsTapped) n++;
        n += p.CostArea.Count(d => d.State == DonState.Active);
        return n;
    }

    /// <summary>「将我方N张卡牌转为休息状态」通用支付：弹窗让玩家从活跃的 领袖/角色/舞台/咚!! 中选 N 张休置，
    /// 四类同列展示（卡牌走卡图、咚走 donChoices token）。候选不足 N 或玩家未选满 → 返回 false（不支付）。
    /// 卡牌走 RestCard（含"无法休息"守护），咚直接置为休息状态。</summary>
    public static async Task<bool> PromptRestOwnCards(EffectContext ctx, int n, string text, bool optional = false)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var cardCands = new List<CardInstance>();
        if (!me.Leader.IsTapped) cardCands.Add(me.Leader);
        cardCands.AddRange(me.Characters.Where(c => !c.IsTapped));
        if (me.StageCard is not null && !me.StageCard.IsTapped) cardCands.Add(me.StageCard);
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
        foreach (var pid in pick)
        {
            var don = activeDon.FirstOrDefault(d => d.Id.ToString() == pid);
            if (don is not null) { don.State = DonState.Rest; continue; }
            var card = cardCands.FirstOrDefault(c => c.Id.ToString() == pid);
            if (card is not null) RestCard(card);
        }
        return true;
    }

    /// <summary>「将对方最多N张卡牌转为休息状态」效果：让玩家从对方活跃的 领袖/角色/舞台/咚!! 中选最多 N 张休置
    /// （min 0，可不选）。四类同列（卡牌走卡图、咚走 donChoices token）。卡牌走 RestCard，咚直接置休息。</summary>
    public static async Task PromptRestOpponentCards(EffectContext ctx, int n)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var cardCands = new List<CardInstance>();
        if (!opp.Leader.IsTapped) cardCands.Add(opp.Leader);
        cardCands.AddRange(opp.Characters.Where(c => !c.IsTapped));
        if (opp.StageCard is not null && !opp.StageCard.IsTapped) cardCands.Add(opp.StageCard);
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
        // 持续"因效果不会被KO"保护（自送废弃/满员牺牲走 KOCard，不经此入口，不受保护）
        if (s.IsKoGuarded(card, "effect")) return;
        if (s.IsLeaveGuarded(card, "effect")) return; // 持续防离场光环（如 EB04-057）
        BattleEngine.KOCard(s, ownerIdx, card);
        EffectRuntime.NotifyWatcher(EffectTrigger.OnCharLeaveField,
            new Dictionary<string, object?> { ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx });
        // 任意角色被KO（效果）：场上他卡可据此反应（如 EB01-047 拉布）
        EffectRuntime.NotifyWatcher(EffectTrigger.OnAnyCharKOd,
            new Dictionary<string, object?> { ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx, ["reason"] = "effect" });
    }

    /// <summary>
    /// 因效果 KO（异步，带置换守护）：相比同步 KO，额外走 PreKO（受害者自身置换）+ OnAllyWillBeKOd（守护者置换）
    /// + 受害者 OnKO 反应，并设置 KO 来源标记供"因对方的效果而被KO"判定。供 DSL 的 KO op 使用，
    /// 覆盖绝大多数效果KO；脚本直接调用的同步 KO 不享守护（已文档化）。
    /// actingSide=发动本次 KO 效果的一方（用于"对方的效果"判定）。返回是否实际 KO。
    /// </summary>
    public static async Task<bool> KOByEffectAsync(GameState s, int ownerIdx, CardInstance card, IPromptService prompts, int actingSide)
    {
        // 设置 KO 来源（effect + 发起方 + 来源卡），供受害者/守护者判定
        s.KOReason = "effect";
        s.KOActingSide = actingSide;
        s.KOSourceCardId = EffectRuntime.CurrentSource?.Id;
        try
        {
            // PreKO：受害者自身"改为…使其不被KO"置换
            s.PreventKOCardIds.Remove(card.Id);
            if (EffectRuntime.HasEffectForTrigger(card, EffectTrigger.PreKO))
                await EffectRuntime.Resolve(s, ownerIdx, card, EffectTrigger.PreKO, prompts);
            if (s.PreventKOCardIds.Contains(card.Id)) { s.PreventKOCardIds.Remove(card.Id); return false; }

            // 守护者：他卡"代替被KO/使其不被KO"置换
            var guardSide = s.Players[ownerIdx];
            var guardians = new List<CardInstance> { guardSide.Leader };
            guardians.AddRange(guardSide.Characters);
            if (guardSide.StageCard is not null) guardians.Add(guardSide.StageCard);
            foreach (var g in guardians.ToList())
            {
                if (g.Id == card.Id) continue;
                if (!EffectRuntime.HasEffectForTrigger(g, EffectTrigger.OnAllyWillBeKOd)) continue;
                await EffectRuntime.Resolve(s, ownerIdx, g, EffectTrigger.OnAllyWillBeKOd, prompts,
                    new Dictionary<string, object?> { ["victimId"] = card.Id.ToString(), ["victimOwner"] = ownerIdx });
                if (s.PreventKOCardIds.Contains(card.Id)) { s.PreventKOCardIds.Remove(card.Id); return false; }
            }

            // 离场守护：他卡"代替离场使其不离场"（KO 属离场的一种；仅"对方效果"触发）
            if (actingSide != ownerIdx)
            {
                s.PreventLeaveCardIds.Remove(card.Id);
                // 不跳过受害卡本身：支持"此角色将要离场时改为…使其不离场"的自我置换
                foreach (var g in guardians.ToList())
                {
                    if (!EffectRuntime.HasEffectForTrigger(g, EffectTrigger.OnAllyWillLeaveField)) continue;
                    await EffectRuntime.Resolve(s, ownerIdx, g, EffectTrigger.OnAllyWillLeaveField, prompts,
                        new Dictionary<string, object?> { ["victimId"] = card.Id.ToString(), ["victimOwner"] = ownerIdx, ["kind"] = "ko" });
                    if (s.PreventLeaveCardIds.Contains(card.Id)) { s.PreventLeaveCardIds.Remove(card.Id); return false; }
                }
            }

            // 持续守护
            if (s.IsKoGuarded(card, "effect")) return false;
            if (s.IsLeaveGuarded(card, "effect")) return false;

            // 实际 KO（复用同步移除逻辑）
            BattleEngine.KOCard(s, ownerIdx, card);
            EffectRuntime.NotifyWatcher(EffectTrigger.OnCharLeaveField,
                new Dictionary<string, object?> { ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx });
            EffectRuntime.NotifyWatcher(EffectTrigger.OnAnyCharKOd,
                new Dictionary<string, object?> { ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx, ["reason"] = "effect" });
            // 受害者 OnKO：卡已进入废弃区，但效果在"原场上位置"发动（如 EB01-057 白星因对方效果被KO）
            await EffectRuntime.Resolve(s, ownerIdx, card, EffectTrigger.OnKO, prompts);
            return true;
        }
        finally
        {
            s.KOReason = null;
            s.KOActingSide = -1;
            s.KOSourceCardId = null;
        }
    }

    /// <summary>
    /// 效果离场置换守护：某卡因"对方效果"将要离开场上(退手牌/回卡组/置入生命等非KO离场)前调用。
    /// 派发 OnAllyWillLeaveField 给受害卡所属方的守护卡(代替离场效果)；若守护卡 MarkPreventLeave 则取消本次离场。
    /// 返回 true=离场被阻止(调用方应跳过本次离场)。仅在"对方效果"(CurrentActingSide 为受害方对手)时生效。
    /// </summary>
    public static async Task<bool> TryEffectLeaveGuard(GameState s, int victimOwner, CardInstance card, IPromptService prompts, string kind)
    {
        int acting = EffectRuntime.CurrentActingSide;
        if (acting < 0 || acting == victimOwner) return false; // 非"对方效果"(或无效果上下文)
        var side = s.Players[victimOwner];
        var guardians = new List<CardInstance> { side.Leader };
        guardians.AddRange(side.Characters);
        if (side.StageCard is not null) guardians.Add(side.StageCard);
        s.PreventLeaveCardIds.Remove(card.Id);
        // 不跳过受害卡本身：支持"此角色将要离场时改为…使其不离场"的自我置换
        foreach (var g in guardians.ToList())
        {
            if (!EffectRuntime.HasEffectForTrigger(g, EffectTrigger.OnAllyWillLeaveField)) continue;
            await EffectRuntime.Resolve(s, victimOwner, g, EffectTrigger.OnAllyWillLeaveField, prompts,
                new Dictionary<string, object?> { ["victimId"] = card.Id.ToString(), ["victimOwner"] = victimOwner, ["kind"] = kind });
            if (s.PreventLeaveCardIds.Contains(card.Id)) { s.PreventLeaveCardIds.Remove(card.Id); return true; }
        }
        return false;
    }

    // ── 关键字 ────────────────────────────────────────────────────────────

    public static void GiveKeyword(CardInstance c, string keyword, KeywordDuration duration, int appliedBy = -1)
    {
        c.GainedKeywords.Add(new TemporaryKeyword { Keyword = keyword, Duration = duration, AppliedBySide = appliedBy });
    }

    // ── 咚操作 ────────────────────────────────────────────────────────────

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
        while (attached < n && p.DonDeck.Count > 0 && p.CostArea.Count < 10)
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
            EffectRuntime.NotifyWatcher(EffectTrigger.OnDonReturnedToDeck,
                new Dictionary<string, object?> { ["count"] = returned });
        return returned;
    }

    /// <summary>
    /// 「咚!!-N」通用支付：让玩家从费用区(活跃/休息/附着在角色·领袖身上)手选 N 张咚放回咚!!卡组。
    /// 合格咚 = 费用区全部状态的咚；不足 N → 返回 false(无法支付，调用方应中止发动)；
    /// 玩家取消/超时同样返回 false。恰好 N 张且全为活跃(无附着)时自动支付，免无意义弹窗。
    /// 放回附着咚会使对应角色/领袖失去贴咚加成(power 由 AttachedDonCount 派生，自动生效)。
    /// </summary>
    public static async Task<bool> PromptReturnDonToDeck(EffectContext ctx, int n)
    {
        if (n <= 0) return true;
        var me = ctx.State.Players[ctx.OwnerIndex];
        // 合格咚 = 费用区全部(活跃 + 休息 + 附着)
        var eligible = me.CostArea
            .Where(d => d.State is DonState.Active or DonState.Rest or DonState.Attached)
            .ToList();
        if (eligible.Count < n) return false;   // 凑不够 → 无法支付

        List<DonCard> chosen;
        // 全为活跃咚时彼此等价、无"该牺牲哪张"的抉择 → 自动支付，免无意义弹窗；
        // 一旦存在休息/附着咚(放回它们有不同代价)，才弹窗让玩家手选。
        bool needPrompt = eligible.Any(d => d.State != DonState.Active);
        if (!needPrompt)
        {
            chosen = eligible.Take(n).ToList();
        }
        else
        {
            // 反查附着目标卡号/名，供客户端标注"贴在 X"
            var donChoices = eligible.Select(d =>
            {
                string? num = null, name = null;
                if (d.State == DonState.Attached && d.AttachedToCardId is { } tid)
                {
                    CardInstance? t = me.Leader.Id == tid
                        ? me.Leader
                        : me.Characters.FirstOrDefault(c => c.Id == tid);
                    if (t is not null) { num = t.Info.Number; name = t.Info.Name; }
                }
                return new
                {
                    id = d.Id.ToString(),
                    state = d.State.ToString(),  // Active / Rest / Attached
                    attachedToNumber = num,
                    attachedToName = name,
                };
            }).ToList();

            var ans = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReturnOwnDon",
                $"选择 {n} 张咚!! 放回咚!!卡组",
                eligible.Select(d => d.Id.ToString()).ToList(), n, n,
                new Dictionary<string, object?> { ["donChoices"] = donChoices });

            if (ans.Count < n) return false;     // 取消/超时 → 不支付
            chosen = new List<DonCard>();
            foreach (var id in ans)
            {
                var d = eligible.FirstOrDefault(x => x.Id.ToString() == id);
                if (d is not null && !chosen.Contains(d)) chosen.Add(d);
                if (chosen.Count >= n) break;
            }
            if (chosen.Count < n) return false;  // 防御：回传 id 对不上
        }

        foreach (var d in chosen)
        {
            d.State = DonState.InDeck;
            d.AttachedToCardId = null;
            me.CostArea.Remove(d);
            me.DonDeck.Add(d);
        }
        EffectRuntime.NotifyWatcher(EffectTrigger.OnDonReturnedToDeck,
            new Dictionary<string, object?> { ["count"] = chosen.Count });
        return true;
    }

    // ── 移动卡牌 ──────────────────────────────────────────────────────────

    /// <summary>把场上一张卡放回手牌</summary>
    public static void BounceToHand(GameState s, int ownerIdx, CardInstance card)
    {
        if (s.IsLeaveGuarded(card, "effect")) return; // 持续防离场光环（如 EB04-057）
        var p = s.Players[ownerIdx];
        // 归还附着咚
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
        // 清除卡的临时状态
        card.IsTapped = false;
        card.PowerModThisTurn = 0;
        card.PowerModThisBattle = 0;
        card.PowerModPersistent = 0;
        card.GainedKeywords.Clear();
        p.Hand.Add(card);
        EffectRuntime.NotifyWatcher(EffectTrigger.OnCharLeaveField,
            new Dictionary<string, object?> { ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx });
    }

    /// <summary>把手牌中的角色卡免费登场</summary>
    public static void PlayFromHandFree(GameState s, int playerIdx, CardInstance card)
    {
        var p = s.Players[playerIdx];
        if (!p.Hand.Remove(card)) return;
        if (card.Info.Kind == CardKind.Character)
        {
            if (p.Characters.Count >= 5)
            {
                var sacrifice = p.Characters[0];
                p.Characters.RemoveAt(0);
                p.Trash.Add(sacrifice);
            }
            card.TurnPlayed = s.TurnCount;
            p.Characters.Add(card);
            s.EnqueueEnterField(playerIdx, card, "hand"); // 触发被登场角色的【登场时】
        }
        else if (card.Info.Kind == CardKind.Stage)
        {
            if (p.StageCard is not null) p.Trash.Add(p.StageCard);
            p.StageCard = card;
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
        for (int i = 0; i < n && p.DonDeck.Count > 0 && p.CostArea.Count < 10; i++)
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
    public static void PlayFromTrashFree(GameState s, int playerIdx, CardInstance card, bool restState = false)
    {
        var p = s.Players[playerIdx];
        if (!p.Trash.Remove(card)) return;
        if (card.Info.Kind == CardKind.Character)
        {
            if (p.Characters.Count >= 5)
            {
                var sacrifice = p.Characters[0];
                p.Characters.RemoveAt(0);
                p.Trash.Add(sacrifice);
            }
            ResetCardEphemeralState(card);
            card.TurnPlayed = s.TurnCount;
            card.IsTapped = restState;
            p.Characters.Add(card);
            s.EnqueueEnterField(playerIdx, card, "trash"); // 触发被登场角色的【登场时】
        }
        else if (card.Info.Kind == CardKind.Stage)
        {
            if (p.StageCard is not null) p.Trash.Add(p.StageCard);
            ResetCardEphemeralState(card);
            p.StageCard = card;
            s.EnqueueEnterField(playerIdx, card, "trash");
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
    public static async Task OpponentDiscardChosen(GameEngine engine, int opponentIdx, int n)
    {
        var opp = engine.State.Players[opponentIdx];
        int actual = Math.Min(n, opp.Hand.Count);
        if (actual <= 0) return;
        var chosen = await engine.Prompts.ChooseCards(opponentIdx, "OwnHandDiscard",
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
        c.GainedKeywords.Clear();
        c.CannotActivateNextReset = false;
        c.OncePerTurnUsedKeys.Clear();
        c.TurnPlayed = 0;
        c.CostModThisTurn = 0;
        c.CostModPersistent = 0;
        c.OriginalPowerOverride = null;
        c.IsEffectsNullified = false;
        c.Restrictions.Clear();
        c.IsLifeFaceUp = false;
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
        ResetCardEphemeralState(card);
        if (toTop) p.LifeArea.Insert(0, card);
        else       p.LifeArea.Add(card);
        EffectRuntime.NotifyWatcher(EffectTrigger.OnCharLeaveField,
            new Dictionary<string, object?> { ["cardId"] = card.Id.ToString(), ["owner"] = ownerIdx });
    }

    /// <summary>把卡组中的一张角色/舞台卡免费登场（登场后触发其【登场时】，见 EnqueueEnterField）。调用方负责洗牌。</summary>
    public static void PlayFromDeckFree(GameState s, int playerIdx, CardInstance card, bool restState = false)
    {
        var p = s.Players[playerIdx];
        if (!p.Deck.Remove(card)) return;
        if (card.Info.Kind == CardKind.Character)
        {
            if (p.Characters.Count >= 5)
            {
                var sacrifice = p.Characters[0];
                p.Characters.RemoveAt(0);
                p.Trash.Add(sacrifice);
            }
            ResetCardEphemeralState(card);
            card.TurnPlayed = s.TurnCount;
            card.IsTapped = restState;
            p.Characters.Add(card);
            s.EnqueueEnterField(playerIdx, card, "deck"); // 触发被登场角色的【登场时】
        }
        else if (card.Info.Kind == CardKind.Stage)
        {
            if (p.StageCard is not null) p.Trash.Add(p.StageCard);
            ResetCardEphemeralState(card);
            p.StageCard = card;
            s.EnqueueEnterField(playerIdx, card, "deck");
        }
    }

    /// <summary>把生命区中的一张角色卡免费登场（登场后触发其【登场时】，见 EnqueueEnterField）。</summary>
    public static void PlayFromLifeFree(GameState s, int playerIdx, CardInstance card, bool restState = false)
    {
        var p = s.Players[playerIdx];
        if (!p.LifeArea.Remove(card)) return;
        if (card.Info.Kind == CardKind.Character)
        {
            if (p.Characters.Count >= 5)
            {
                var sacrifice = p.Characters[0];
                p.Characters.RemoveAt(0);
                p.Trash.Add(sacrifice);
            }
            ResetCardEphemeralState(card);
            card.TurnPlayed = s.TurnCount;
            card.IsTapped = restState;
            p.Characters.Add(card);
            s.EnqueueEnterField(playerIdx, card, "life"); // 触发被登场角色的【登场时】
        }
        else
        {
            // 非角色卡无法登场到角色区：退回生命底，避免丢失
            p.LifeArea.Add(card);
        }
    }

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
        return picked;
    }

    /// <summary>给卡加费用修正</summary>
    public static void AddCostModifier(CardInstance c, int delta, KeywordDuration duration)
    {
        if (duration == KeywordDuration.UntilNextOpponentEndPhase) c.CostModPersistent += delta;
        else c.CostModThisTurn += delta;
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
