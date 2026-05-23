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
}
