using GrandUMI.Effects;
using GrandUMI.Cards;

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
        DeckOutRules.Arm(state);
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

        // 新的自己回合开始：仍在场的卡重新获得【每回合1次】可用标识。
        // 对方回合内使用过的防御型效果也会在其控制者下次回合开始恢复。
        p.OncePerTurnEffectUsedCardIds.Clear();

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
        if (p.Leader.CannotActivateNextReset)
            p.Leader.CannotActivateNextReset = false;
        else if (!state.IsResetPrevented(p.Leader) && p.Leader.IsTapped)
            p.Leader.IsTapped = false;
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
        if (p.StageCard is { } stage)
        {
            if (stage.CannotActivateNextReset) stage.CannotActivateNextReset = false;
            else if (stage.IsTapped && !state.IsResetPrevented(stage)) stage.IsTapped = false;
        }
        if (p.ExtraStageCard is { } extraStage)
        {
            if (extraStage.CannotActivateNextReset) extraStage.CannotActivateNextReset = false;
            else if (extraStage.IsTapped && !state.IsResetPrevented(extraStage)) extraStage.IsTapped = false;
        }
        foreach (var d in p.CostArea)
            if (d.State == DonState.Rest)
            {
                if (d.CannotActivateNextReset) { d.CannotActivateNextReset = false; continue; } // 一次性：此咚跳过激活
                d.State = DonState.Active;
            }

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
        var tp = state.Players[state.CurrentTurnPlayer];
        int donBefore = tp.TotalDonInCostArea;
        int added = AddDonFromDeck(state, state.CurrentTurnPlayer, add);

        // OP13-003 高路·D·罗杰：进入咚阶段前场上已存在咚，且本次确实会放置咚时，
        // 才把其中 1 张改为赋予领袖。0 咚的首回合与已经 10 咚时都不会发动。
        if (tp.Leader.Info.Number == "OP13-003"
            && !tp.Leader.IsEffectsNullified
            && !state.IsContinuouslyNullified(tp.Leader)
            && donBefore > 0
            && donBefore < state.MaxDonInCostAreaFor(state.CurrentTurnPlayer)
            && added > 0)
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
            // 规则层的【每回合1次】在每个回合结束后恢复；界面标识的恢复时点独立为控制者自己回合开始。
            p.TurnOnceUsed.Clear();
            p.HandDiscardedByEffectThisTurn = false;
            p.HasActivatedBaseCost3PlusEventThisTurn = false;
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
                // 回合结束时将指定数量的我方休息咚转为活跃状态（默认1张；OP14-031为5张）
                var owner = state.Players[task.Owner];
                foreach (var don in owner.CostArea
                             .Where(d => d.State == DonState.Rest && d.AttachedToCardId is null)
                             .Take(task.Count))
                    don.State = DonState.Active;
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
            else if (task.Kind == "RefreshAllFilmCharacters")
            {
                // P-058：本回合结束时，将我方所有《FILM》角色转为活跃状态。
                foreach (var card in state.Players[task.Owner].Characters.Where(c => c.Info.HasKeyword("FILM")))
                    card.IsTapped = false;
            }
            else if (task.Kind == "ReturnCharacterToDeckBottom")
            {
                var owner = state.Players[task.Owner];
                var card = owner.Characters.FirstOrDefault(c => c.Id.ToString() == task.SourceCardId);
                if (card is not null)
                    AtomicOps.ReturnFieldToDeckBottom(state, task.Owner, card);
            }
        }
        state.EndOfTurnTasks.Clear();
        // 清除回合级玩家状态
        state.NoPlayCharacterThisTurn.Clear();
        state.NoPlayCharacterOriginalCostGteThisTurn.Clear();
        state.NoEffectLifeToHandThisTurn.Clear();
        state.NoActivateDonByCharacterEffectThisTurn.Clear();
        state.LifeLeftThisTurn.Clear();
        state.OneShotPlayDiscounts.Clear();   // OP02-025：一次性减费仅"本回合下次"
        state.AttackTaxDiscard[state.CurrentTurnPlayer] = 0; // 本回合方被课的攻击税到期

        // 清除到期或来源已不在场上的 ContinuousEffect（防止僵尸效果）。
        state.ContinuousEffects.RemoveAll(eff =>
            eff.ExpiresAtEndOfTurnForSide == state.CurrentTurnPlayer
            || !IsSourceCardOnField(state, eff.SourceCardId));
    }

    /// <summary>
    /// 在同步结束阶段清理前，先处理需要玩家确认的延迟离场任务。
    /// 这类任务不能直接调用同步移动方法，否则“将要离场时”的置换效果没有机会弹出并支付成本。
    /// </summary>
    public static async Task ResolvePromptedEndPhaseTasksAsync(GameState state, IPromptService prompts)
    {
        var tasks = state.EndOfTurnTasks
            .Where(task => task.Kind is "ReturnCharacterToDeckBottom"
                or "PreventOpponentDonCharacterReset"
                or "ReturnExcessDonToOpponentCount"
                or "ChooseRefreshOwnDonUpTo")
            .ToList();
        foreach (var task in tasks)
        {
            state.EndOfTurnTasks.Remove(task);

            if (task.Kind == "ChooseRefreshOwnDonUpTo")
            {
                await AtomicOps.PromptChooseAndApplyDonCount(
                    state,
                    prompts,
                    task.Owner,
                    task.Count,
                    $"奈美【回合结束时】：选择要转为活跃状态的休息咚!!张数（最多 {task.Count} 张）",
                    don => don.State == DonState.Rest && don.AttachedToCardId is null,
                    don => don.State = DonState.Active);
                continue;
            }

            if (task.Kind == "ReturnExcessDonToOpponentCount")
            {
                var returnOwner = state.Players[task.Owner];
                var source = returnOwner.Characters
                    .Concat(returnOwner.Hand)
                    .Concat(returnOwner.Trash)
                    .Concat(returnOwner.Deck)
                    .Concat(returnOwner.LifeArea)
                    .Append(returnOwner.StageCard)
                    .Append(returnOwner.ExtraStageCard)
                    .FirstOrDefault(card => card?.Id.ToString() == task.SourceCardId);
                if (source is null) continue;

                await EffectRuntime.Resolve(
                    state,
                    task.Owner,
                    source,
                    EffectTrigger.OnMyTurnEnd,
                    prompts,
                    new Dictionary<string, object?>
                {
                    ["scheduled"] = true,
                });
                continue;
            }

            if (task.Kind == "PreventOpponentDonCharacterReset")
            {
                var opponent = state.Players[1 - task.Owner];
                var lockable = opponent.Characters
                    .Where(card => card.IsTapped && opponent.AttachedDonCount(card.Id) >= 3)
                    .ToList();
                if (lockable.Count == 0) continue;

                var extra = new Dictionary<string, object?>
                {
                    ["choiceCards"] = lockable
                        .Select(card => new { id = card.Id.ToString(), number = card.Info.Number })
                        .ToList(),
                };
                var picked = await prompts.ChooseCards(
                    task.Owner,
                    "OpponentCharacter",
                    "选择对方最多一张被赋予三张或更多咚且处于休息状态的角色，使其在下个重置阶段不转为活跃",
                    lockable.Select(card => card.Id.ToString()).ToList(),
                    0,
                    1,
                    extra);
                if (picked.Count > 0)
                {
                    var locked = lockable.First(card => card.Id.ToString() == picked[0]);
                    AtomicOps.PreventActivateNextReset(locked);
                }
                continue;
            }

            var owner = state.Players[task.Owner];
            var card = owner.Characters.FirstOrDefault(c => c.Id.ToString() == task.SourceCardId);
            if (card is null) continue;
            await AtomicOps.ProcessEffectLeavesAsync(
                state,
                task.Owner,
                new[] { card },
                prompts,
                "deckBottom",
                AtomicOps.ReturnFieldToDeckBottom);
        }
    }

    private static void ClearTurnScopedState(CardInstance c)
    {
        c.PowerModThisTurn = 0;
        c.PowerModThisBattle = 0;
        c.CostModThisTurn = 0;
        c.OriginalPowerOverride = null;
        c.IsEffectsNullified = false;
        c.NoAttackCostLeThisTurn = 0;
        c.BattledOpponentCharacterThisTurn = false;
        c.GainedPropertiesThisTurn.Clear();
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
        // 力量修正（"直到下个对方结束阶段力量±N"，如 OP16-065）同理清除
        c.PowerModsUntilOppEnd.RemoveAll(m =>
        {
            if (m.AppliedBySide >= 0) return currentTurnPlayer == 1 - m.AppliedBySide;
            if (m.EndPhasesSeen == 0) { m.EndPhasesSeen = 1; return false; }
            return true;
        });
        c.OriginalPowerOverridesUntilOppEnd.RemoveAll(m =>
        {
            if (m.AppliedBySide >= 0) return currentTurnPlayer == 1 - m.AppliedBySide;
            if (m.EndPhasesSeen == 0) { m.EndPhasesSeen = 1; return false; }
            return true;
        });
        c.CostModsUntilOppEnd.RemoveAll(m =>
        {
            bool expired;
            if (m.AppliedBySide >= 0)
            {
                expired = currentTurnPlayer == 1 - m.AppliedBySide;
            }
            else if (m.EndPhasesSeen == 0)
            {
                m.EndPhasesSeen = 1;
                expired = false;
            }
            else
            {
                expired = true;
            }

            if (expired) c.CostModPersistent -= m.Delta;
            return expired;
        });
    }

    private static bool IsSourceCardOnField(GameState s, string sourceId)
    {
        // 限时效果可在来源 GUID 后附加用途后缀，仍应按前 36 位定位来源卡。
        if (sourceId.Length < 36 || !Guid.TryParse(sourceId[..36], out var gid)) return false;
        foreach (var p in s.Players)
        {
            if (p.Leader.Id == gid) return true;
            if (p.StageCard?.Id == gid) return true;
            if (p.ExtraStageCard?.Id == gid) return true;
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
        StartNextTurnAtReset(state);
    }

    /// <summary>当前结束阶段已完成清理后，原子切换回合方并执行新回合准备阶段。</summary>
    public static void StartNextTurnAtReset(GameState state)
    {
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
        int kept = 0;
        while (kept < n)
        {
            if (p.Deck.Count == 0)
            {
                state.EvaluateDeckOut();
                break;
            }
            var top = p.Deck[0];
            p.Deck.RemoveAt(0);
            var runtime = state.HexState.Runtime[playerIdx];
            bool useLegacyHexConversion = state.HexState.RulesRevision < Hex.HexRules.SevenHexReworkRulesRevision;
            bool convertEvent = useLegacyHexConversion
                && top.Info.Kind == CardKind.Event
                && Hex.HexRules.Has(state, playerIdx, 38)
                && !runtime.EventDrawConvertedThisTurn;
            bool convertCharacter = useLegacyHexConversion
                && top.Info.Kind == CardKind.Character
                && Hex.HexRules.Has(state, playerIdx, 39)
                && !runtime.CharacterDrawConvertedThisTurn;
            if (convertEvent || convertCharacter)
            {
                if (convertEvent) runtime.EventDrawConvertedThisTurn = true;
                if (convertCharacter) runtime.CharacterDrawConvertedThisTurn = true;
                p.Trash.Add(top);
            }
            else
            {
                p.Hand.Add(top);
                kept++;
            }
            // 正式对局已启用规则时，抽走最后一张后立即判定；测试布场在未启用规则前不会被误判。
            state.EvaluateDeckOut();
            if (state.IsGameOver) break;
        }
        return kept;
    }

    /// <summary>从咚卡组取 n 张到费用区（活跃状态）</summary>
    public static int AddDonFromDeck(GameState state, int playerIdx, int n)
    {
        var p = state.Players[playerIdx];
        int actual = 0;
        int budget = state.MaxDonInCostAreaFor(playerIdx) - p.CostArea.Count;
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
