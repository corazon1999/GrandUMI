namespace GrandUMI.Game.PhaseFlow;

/// <summary>
/// 5 阶段流程驱动：Reset → Draw → Don → Main → End
/// 各阶段进入时执行强制规则处理（恢复活跃、抽牌、加咚等），
/// 主要阶段的玩家自主操作由 GameEngine 处理。
/// </summary>
public static class TurnEngine
{
    public const int MaxDonInCostArea = 10;

    /// <summary>开局后双方都完成 Mulligan 时，进入第一回合的 Reset 阶段</summary>
    public static void StartFirstTurn(GameState state)
    {
        state.TurnCount = 1;
        state.CurrentTurnPlayer = state.FirstPlayer;
        state.Phase = Phase.Reset;
        EnterResetPhase(state); // 重置阶段是空操作（角色都还没登场），紧接 Draw / Don
        EnterDrawPhase(state);
        EnterDonPhase(state);
        state.Phase = Phase.Main;
    }

    public static void EnterResetPhase(GameState state)
    {
        state.Phase = Phase.Reset;
        var p = state.Turn;

        // 1. 赋予中的咚 → 休息状态放回费用区
        foreach (var d in p.CostArea)
        {
            if (d.State == DonState.Attached)
            {
                d.State = DonState.Rest;
                d.AttachedToCardId = null;
            }
        }

        // 2. 领袖/角色/舞台/费用区中的休息卡牌 → 活跃
        if (p.Leader.IsTapped) p.Leader.IsTapped = false;
        foreach (var c in p.Characters)
        {
            if (c.CannotActivateNextReset)
            {
                c.CannotActivateNextReset = false; // 一次性，使用后清除
                continue;                          // 跳过本次激活
            }
            if (state.IsResetPrevented(c)) continue;   // 持续"无法转为活跃"
            if (c.IsTapped) c.IsTapped = false;
        }
        // 舞台卡通常不会休息，但留个处理点
        if (p.StageCard is { IsTapped: true } && !state.IsResetPrevented(p.StageCard)) p.StageCard.IsTapped = false;
        foreach (var d in p.CostArea)
            if (d.State == DonState.Rest)
            {
                if (d.CannotActivateNextReset) { d.CannotActivateNextReset = false; continue; } // 一次性：此咚跳过激活
                d.State = DonState.Active;
            }

        // 清除本回合"每回合 1 次"使用记录（在 EnterResetPhase 阶段，回合玩家自己的清掉，
        // 实际官方规则是在结束阶段清除，统一在 EnterEndPhase 做）
    }

    public static void EnterDrawPhase(GameState state)
    {
        state.Phase = Phase.Draw;
        // 第一回合先手不抽
        bool firstTurnFirstPlayer = state.TurnCount == 1 && state.CurrentTurnPlayer == state.FirstPlayer;
        if (!firstTurnFirstPlayer)
            DrawCard(state, state.CurrentTurnPlayer, 1);
    }

    public static void EnterDonPhase(GameState state)
    {
        state.Phase = Phase.Don;
        bool firstTurnFirstPlayer = state.TurnCount == 1 && state.CurrentTurnPlayer == state.FirstPlayer;
        int add = firstTurnFirstPlayer ? 1 : 2;
        AddDonFromDeck(state, state.CurrentTurnPlayer, add);

        // OP13-003 高路·D·罗杰：我方场上存在咚时，将1张将要放置的咚改赋予我方领袖
        var tp = state.Players[state.CurrentTurnPlayer];
        if (tp.Leader.Info.Number == "OP13-003")
        {
            var don = tp.CostArea.FirstOrDefault(d => d.State == DonState.Active);
            if (don is not null) { don.State = DonState.Attached; don.AttachedToCardId = tp.Leader.Id; }
        }
    }

    public static void EnterEndPhase(GameState state)
    {
        state.Phase = Phase.End;
        // 清除本回合内的修正 + 期限关键字（含"直到下个对方的结束阶段结束时为止"）
        foreach (var p in state.Players)
        {
            foreach (var c in p.Characters)
            {
                ClearTurnScopedState(c);
                ClearUntilNextOpponentEndPhase(c, state.CurrentTurnPlayer);
            }
            ClearTurnScopedState(p.Leader);
            ClearUntilNextOpponentEndPhase(p.Leader, state.CurrentTurnPlayer);
            // 手牌卡的本回合费用修正（如 OP16-087 给"光月桃之助"+20）也需回合末清，否则会跨回合残留
            foreach (var c in p.Hand) c.CostModThisTurn = 0;
            p.TurnOnceUsed.Clear();
        }
        // 延迟到回合结束执行的任务（如 OP06-006：将我方1张FILM角色放置废弃区）
        foreach (var task in state.EndOfTurnTasks)
        {
            if (task.Kind == "TrashFilm")
            {
                var owner = state.Players[task.Owner];
                // 优先废弃来源卡(其本身为FILM)，否则任意一张FILM角色
                var victim = owner.Characters.FirstOrDefault(c => c.Id.ToString() == task.SourceCardId)
                          ?? owner.Characters.FirstOrDefault(c => c.Info.HasKeyword("FILM"));
                if (victim is not null) { owner.Characters.Remove(victim); owner.Trash.Add(victim); }
            }
            else if (task.Kind == "RefreshOwnDon")
            {
                // EB02-015：回合结束时将我方最多 1 张休息咚转为活跃状态
                var owner = state.Players[task.Owner];
                var don = owner.CostArea.FirstOrDefault(d => d.State == DonState.Rest && d.AttachedToCardId is null);
                if (don is not null) don.State = DonState.Active;
            }
            else if (task.Kind == "ReturnSelfToHand")
            {
                // OP04-009：回合结束时将来源角色放回持有者手牌
                var owner = state.Players[task.Owner];
                var self = owner.Characters.FirstOrDefault(c => c.Id.ToString() == task.SourceCardId);
                if (self is not null)
                {
                    // 归还附着咚
                    foreach (var d in owner.CostArea)
                        if (d.State == DonState.Attached && d.AttachedToCardId == self.Id)
                        { d.State = DonState.Rest; d.AttachedToCardId = null; }
                    owner.Characters.Remove(self);
                    owner.Hand.Add(self);
                }
            }
        }
        state.EndOfTurnTasks.Clear();
        // 清除回合级玩家状态
        state.NoPlayCharacterThisTurn.Clear();
        state.NoEffectLifeToHandThisTurn.Clear();
        state.OneShotPlayDiscounts.Clear();   // OP02-025：一次性减费仅"本回合下次"
        state.AttackTaxDiscard[state.CurrentTurnPlayer] = 0; // 本回合方被课的攻击税到期

        // 清除来源已不在场上的 ContinuousEffect（防止僵尸效果）
        state.ContinuousEffects.RemoveAll(eff => !IsSourceCardOnField(state, eff.SourceCardId));
    }

    private static void ClearTurnScopedState(CardInstance c)
    {
        c.PowerModThisTurn = 0;
        c.PowerModThisBattle = 0;
        c.CostModThisTurn = 0;
        c.OriginalPowerOverride = null;
        c.IsEffectsNullified = false;
        c.NoAttackCostLeThisTurn = 0;
        c.ReactivateAfterBattleThisTurn = false;
        // 注意：UntilNextOpponentEndPhase 不在此清除——它须保留到"对方"的结束阶段，
        // 否则会在施加者自己的结束阶段就失效（#146/#147）。改由 ClearUntilNextOpponentEndPhase 处理。
        c.GainedKeywords.RemoveAll(k =>
            k.Duration == KeywordDuration.ThisTurn ||
            k.Duration == KeywordDuration.ThisBattle);
        c.Restrictions.RemoveAll(r =>
            r.Duration == KeywordDuration.ThisTurn ||
            r.Duration == KeywordDuration.ThisBattle);
        c.OncePerTurnUsedKeys.Clear();
    }

    /// <summary>
    /// 清除"直到下个对方的结束阶段结束时为止"(UntilNextOpponentEndPhase) 的关键词 / 限制。
    /// 语义：由控制者一方 S 施加，应持续到 S 的对手(1-S)的结束阶段结束。
    ///   - AppliedBySide 已指定：仅当本结束阶段属于对手(CurrentTurnPlayer == 1-S)时清除；
    ///     S 自己的结束阶段保留（这正是修复点——此前会在 S 的结束阶段被误清）。
    ///   - AppliedBySide 未指定(-1，旧调用方)：回退为"生存一个结束阶段"——首个结束阶段保留、
    ///     第二个结束阶段清除。对绝大多数"我方回合发动"的效果与上面等价。
    /// </summary>
    private static void ClearUntilNextOpponentEndPhase(CardInstance c, int currentTurnPlayer)
    {
        c.GainedKeywords.RemoveAll(k =>
        {
            if (k.Duration != KeywordDuration.UntilNextOpponentEndPhase) return false;
            if (k.AppliedBySide >= 0) return currentTurnPlayer == 1 - k.AppliedBySide;
            if (k.EndPhasesSeen == 0) { k.EndPhasesSeen = 1; return false; }
            return true;
        });
        c.Restrictions.RemoveAll(r =>
        {
            if (r.Duration != KeywordDuration.UntilNextOpponentEndPhase) return false;
            if (r.AppliedBySide >= 0) return currentTurnPlayer == 1 - r.AppliedBySide;
            if (r.EndPhasesSeen == 0) { r.EndPhasesSeen = 1; return false; }
            return true;
        });
    }

    private static bool IsSourceCardOnField(GameState s, string sourceId)
    {
        if (!Guid.TryParse(sourceId, out var gid)) return false;
        foreach (var p in s.Players)
        {
            if (p.Leader.Id == gid) return true;
            if (p.StageCard?.Id == gid) return true;
            foreach (var c in p.Characters) if (c.Id == gid) return true;
        }
        return false;
    }

    /// <summary>由主要阶段宣言结束回合时调用：执行结束阶段，然后切到对方的 Reset → Draw → Don → Main。
    /// 整段版本；保留给无需在「回合开始时」插入异步事件的调用方（测试等）。</summary>
    public static void AdvanceTurn(GameState state)
    {
        AdvanceTurnToReset(state);
        AdvanceTurnToMain(state);
    }

    /// <summary>回合推进前半段：结束阶段 → 切换回合玩家 → 准备阶段(Reset)。停在 Reset 之后、抽牌之前，
    /// 便于调用方在此处异步派发【我方回合开始时】(OnTurnStart)；此刻费用区咚数 = 进入本回合时的咚总数
    /// （本回合 Don 阶段尚未加咚），符合「回合开始时」的判定时点（如 OP11-040 路飞场上咚≥8）。</summary>
    public static void AdvanceTurnToReset(GameState state)
    {
        EnterEndPhase(state);
        // 切换回合玩家（"追加获得我方回合"时跳过切换，同一玩家再来一回合）
        if (state.ExtraTurnPending) state.ExtraTurnPending = false;
        else state.CurrentTurnPlayer = 1 - state.CurrentTurnPlayer;
        state.TurnCount += 1;
        EnterResetPhase(state);
    }

    /// <summary>回合推进后半段：抽牌阶段 → 咚阶段 → 进入主要阶段。</summary>
    public static void AdvanceTurnToMain(GameState state)
    {
        EnterDrawPhase(state);
        EnterDonPhase(state);
        state.Phase = Phase.Main;
    }

    /// <summary>从卡组抽 n 张</summary>
    public static int DrawCard(GameState state, int playerIdx, int n)
    {
        var p = state.Players[playerIdx];
        int actual = 0;
        for (int i = 0; i < n; i++)
        {
            if (p.Deck.Count == 0)
            {
                // 卡组抽空 → 规则处理时点判负；OP03-040 奈美：登记了"卡组0张改判胜利"则反判此玩家胜利
                if (!state.IsGameOver)
                {
                    bool deckOutWin = state.DeckOutVictoryPlayers.Contains(playerIdx);
                    state.WinnerIndex = deckOutWin ? playerIdx : 1 - playerIdx;
                    state.GameOverReason = deckOutWin
                        ? $"{p.AccountName} 卡组耗尽（规则替换：胜利）"
                        : $"{p.AccountName} 卡组耗尽";
                }
                break;
            }
            var top = p.Deck[0];
            p.Deck.RemoveAt(0);
            p.Hand.Add(top);
            actual++;
        }
        return actual;
    }

    /// <summary>从咚卡组取 n 张到费用区（活跃状态）</summary>
    public static int AddDonFromDeck(GameState state, int playerIdx, int n)
    {
        var p = state.Players[playerIdx];
        int actual = 0;
        int budget = MaxDonInCostArea - p.CostArea.Count;
        n = Math.Min(n, budget);
        for (int i = 0; i < n; i++)
        {
            if (p.DonDeck.Count == 0) break;
            var d = p.DonDeck[0];
            p.DonDeck.RemoveAt(0);
            d.State = DonState.Active;
            d.AttachedToCardId = null;
            p.CostArea.Add(d);
            actual++;
        }
        return actual;
    }
}
