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
        => TurnEngine.DrawCard(s, playerIdx, n);

    public static void DiscardHand(PlayerState p, CardInstance card)
    {
        p.Hand.Remove(card);
        p.Trash.Add(card);
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

    public static void RestCard(CardInstance c) { c.IsTapped = true; }
    public static void ActivateCard(CardInstance c) { c.IsTapped = false; }

    /// <summary>标记下个重置阶段不会转活跃</summary>
    public static void PreventActivateNextReset(CardInstance c)
        => c.CannotActivateNextReset = true;

    // ── KO ────────────────────────────────────────────────────────────────

    public static void KO(GameState s, int ownerIdx, CardInstance card)
        => BattleEngine.KOCard(s, ownerIdx, card);

    // ── 关键字 ────────────────────────────────────────────────────────────

    public static void GiveKeyword(CardInstance c, string keyword, KeywordDuration duration)
    {
        c.GainedKeywords.Add(new TemporaryKeyword { Keyword = keyword, Duration = duration });
    }

    // ── 咚操作 ────────────────────────────────────────────────────────────

    /// <summary>从费用区选 n 张咚（按 fromState 状态）附给 target</summary>
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
        return returned;
    }

    // ── 移动卡牌 ──────────────────────────────────────────────────────────

    /// <summary>把场上一张卡放回手牌</summary>
    public static void BounceToHand(GameState s, int ownerIdx, CardInstance card)
    {
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
        }
        else if (card.Info.Kind == CardKind.Stage)
        {
            if (p.StageCard is not null) p.Trash.Add(p.StageCard);
            p.StageCard = card;
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
        }
        else if (card.Info.Kind == CardKind.Stage)
        {
            if (p.StageCard is not null) p.Trash.Add(p.StageCard);
            ResetCardEphemeralState(card);
            p.StageCard = card;
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
            Shuffle(p.Deck);
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
        Shuffle(p.Deck);
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
    public static void AddRestriction(CardInstance c, RestrictionKind kind, KeywordDuration duration)
    {
        c.Restrictions.Add(new CardRestriction { Kind = kind, Duration = duration });
    }

    /// <summary>洗牌（Fisher–Yates）</summary>
    private static readonly Random _rng = new();
    public static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
