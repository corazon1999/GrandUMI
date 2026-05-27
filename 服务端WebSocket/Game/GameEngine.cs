using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using System.Security.Cryptography;
using System.Text.Json;

namespace GrandUMI.Game;

/// <summary>
/// 单个房间的对战引擎（线程不安全；所有调用需在 GameRoomManager 中串行化）
/// </summary>
public class GameEngine
{
    public GameState State { get; }
    public PromptSystem Prompts { get; }
    public Action<int, object>? OnSendToPlayer { get; set; }   // (playerIndex, payload)
    public Action<object>?      OnBroadcast    { get; set; }   // 双方都收到
    public Action<object>?      OnReplay       { get; set; }   // 写入回放
    public Action<string, int?, object?>? OnMatchLog { get; set; }
    public Action<int, object>? OnSendToSpectators { get; set; } // 观战推送（spectator 视角）
    private string? _activeAction;
    private int? _activeActor;
    private bool _activeActionRejected;
    private readonly Random _rng;
    private readonly List<(string Kind, int? Actor, object? Payload)> _pendingMatchLogs = new();

    /// <summary>
    /// 用双方 deck 字符串构造引擎（已通过 DeckValidator 校验）
    /// firstPlayer = 先手玩家索引 (0/1)
    /// </summary>
    public GameEngine(string roomId, (string sessionId, string accountName, string deckRaw) p0,
                                       (string sessionId, string accountName, string deckRaw) p1,
                                       int firstPlayer,
                                       int? rngSeed = null)
    {
        var seed = rngSeed ?? RandomNumberGenerator.GetInt32(int.MaxValue);
        _rng = new Random(seed);
        State = new GameState { RoomId = roomId, FirstPlayer = firstPlayer, RngSeed = seed };

        var p0Cards = ParseDeck(p0.deckRaw, out var p0Leader);
        var p1Cards = ParseDeck(p1.deckRaw, out var p1Leader);

        var player0 = new PlayerState
        {
            SessionId   = p0.sessionId,
            AccountName = p0.accountName,
            Leader      = new CardInstance { Info = p0Leader },
        };
        player0.Deck.AddRange(p0Cards);
        InitDonDeck(player0);
        InitLifeAndHand(player0, 0);

        var player1 = new PlayerState
        {
            SessionId   = p1.sessionId,
            AccountName = p1.accountName,
            Leader      = new CardInstance { Info = p1Leader },
        };
        player1.Deck.AddRange(p1Cards);
        InitDonDeck(player1);
        InitLifeAndHand(player1, 1);

        State.Players[0] = player0;
        State.Players[1] = player1;
        State.CurrentTurnPlayer = firstPlayer;
        State.Phase = Phase.Reset;
        State.TurnCount = 0; // 在双方完成 mulligan 后调用 TurnEngine.StartFirstTurn 才进入 turn 1
        Prompts = new PromptSystem(this);
    }

    // ── 引擎入口 ──────────────────────────────────────────────────────────

    public void HandleAction(int playerIndex, string action, JsonElement data)
    {
        if (State.IsGameOver) return;

        _activeAction = action;
        _activeActor = playerIndex;
        _activeActionRejected = false;

        switch (action)
        {
            case "Mulligan":       HandleMulligan(playerIndex, data); break;
            case "PlayCard":       HandlePlayCard(playerIndex, data); break;
            case "AttachDon":      HandleAttachDon(playerIndex, data); break;
            case "Attack":         HandleAttack(playerIndex, data); break;
            case "DeclareBlocker": HandleDeclareBlocker(playerIndex, data); break;
            case "PassBlock":      HandlePassBlock(playerIndex); break;
            case "PlayCounter":    HandlePlayCounter(playerIndex, data); break;
            case "PassCounter":    HandlePassCounter(playerIndex); break;
            case "EndTurn":        HandleEndTurn(playerIndex); break;
            case "Surrender":      HandleSurrender(playerIndex); break;
            case "PromptResponse": HandlePromptResponse(playerIndex, data); break;
            case "UseEffect":      HandleUseEffect(playerIndex, data); break;
            default:
                SendError(playerIndex, $"未知动作: {action}");
                break;
        }

        if (!_activeActionRejected)
            RecordMatchLog("player_action_accepted", playerIndex, new { action });

        _activeAction = null;
        _activeActor = null;
    }

    // ── 出牌 ───────────────────────────────────────────────────────────────

    private void HandlePlayCard(int playerIndex, JsonElement data)
    {
        if (!data.TryGetProperty("handIndex", out var hi) || hi.ValueKind != JsonValueKind.Number)
        { SendError(playerIndex, "缺少 handIndex"); return; }
        int handIndex = hi.GetInt32();

        var v = ActionValidator.CanPlayCard(State, playerIndex, handIndex);
        if (!v.Ok) { SendError(playerIndex, v.Reason!); return; }

        var p = State.Players[playerIndex];
        var cardNumber = p.Hand[handIndex].Info.Number;
        var result = CardPlayer.Play(State, playerIndex, handIndex);
        Broadcast("PlayCard", new { player = playerIndex, cardNumber, kind = result.Kind.ToString(), cardId = result.Card.Id.ToString() });

        // 触发对应效果
        _ = ResolveEffectAsync(playerIndex, result);
    }

    private async Task ResolveEffectAsync(int playerIndex, PlayResult result)
    {
        try
        {
            if (result.Kind == PlayKind.Character || result.Kind == PlayKind.Stage)
            {
                await EffectRuntime.Resolve(State, playerIndex, result.Card, EffectTrigger.OnEnterField, Prompts);
            }
            else if (result.Kind == PlayKind.Event)
            {
                await EffectRuntime.Resolve(State, playerIndex, result.Card, EffectTrigger.EventMain, Prompts);
            }
            Broadcast("EffectResolved", new { cardNumber = result.Card.Info.Number });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Effects] {result.Card.Info.Number} 解析异常: {ex.Message}");
        }
    }

    // ── 赋予咚 ────────────────────────────────────────────────────────────

    private void HandleAttachDon(int playerIndex, JsonElement data)
    {
        if (!data.TryGetProperty("targetId", out var ti) || ti.ValueKind != JsonValueKind.String)
        { SendError(playerIndex, "缺少 targetId"); return; }
        var targetIdStr = ti.GetString()!;
        var v = ActionValidator.CanAttachDon(State, playerIndex, targetIdStr);
        if (!v.Ok) { SendError(playerIndex, v.Reason!); return; }

        var p = State.Players[playerIndex];
        Guid targetId = targetIdStr == "leader" ? p.Leader.Id : Guid.Parse(targetIdStr);
        int count = data.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 1;
        for (int i = 0; i < count; i++) CardPlayer.AttachDon(p, targetId);
        Broadcast("AttachDon", new { player = playerIndex, targetId = targetIdStr, count });
    }

    // ── 攻击 ───────────────────────────────────────────────────────────────

    private void HandleAttack(int playerIndex, JsonElement data)
    {
        if (!data.TryGetProperty("attackerId", out var aid)) { SendError(playerIndex, "缺少 attackerId"); return; }
        var attackerStr = aid.GetString() ?? "";
        if (!Guid.TryParse(attackerStr, out var attackerId)) { SendError(playerIndex, "attackerId 非法"); return; }

        bool targetIsLeader = data.TryGetProperty("targetIsLeader", out var til) && til.ValueKind == JsonValueKind.True;
        Guid? targetId = null;
        if (!targetIsLeader)
        {
            if (!data.TryGetProperty("targetId", out var tid)) { SendError(playerIndex, "缺少 targetId"); return; }
            if (!Guid.TryParse(tid.GetString() ?? "", out var gid)) { SendError(playerIndex, "targetId 非法"); return; }
            targetId = gid;
        }

        var v = ActionValidator.CanAttack(State, playerIndex, attackerId, targetIsLeader, targetId);
        if (!v.Ok) { SendError(playerIndex, v.Reason!); return; }

        BattleEngine.StartAttack(State, attackerId, targetIsLeader, targetId);
        Broadcast("Attack", new { attacker = attackerStr, targetIsLeader, targetId = targetId?.ToString() });

        // 异步推进战斗：触发【攻击时】→ 判断 Block → 判断 Counter → 伤害结算
        _ = AdvanceBattleAfterAttackDeclareAsync(playerIndex);
    }

    private async Task AdvanceBattleAfterAttackDeclareAsync(int attackerIdx)
    {
        try
        {
            await BattleEngine.TriggerAttackDeclareAsync(State, Prompts);
            if (State.IsGameOver || State.CurrentBattle is null) { CheckGameOver(); return; }

            // 若防守方无可用【阻挡者】（攻击者带【不可阻挡】也跳过 Block）
            var def = State.Players[1 - attackerIdx];
            var atk = State.Players[attackerIdx];
            var attackerCard = atk.Leader.Id == State.CurrentBattle.AttackerCardId ? atk.Leader
                : atk.Characters.FirstOrDefault(c => c.Id == State.CurrentBattle.AttackerCardId);
            bool attackerUnblockable = attackerCard is not null && ActionValidator.HasKeyword(attackerCard, "不可阻挡");
            bool hasBlocker = !attackerUnblockable && def.Characters.Any(c => !c.IsTapped && ActionValidator.HasKeyword(c, "阻挡者"));
            if (!hasBlocker)
            {
                BattleEngine.PassBlock(State);
                Broadcast("AutoPassBlock");
                await AdvanceBattleAfterBlockAsync();
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Battle] AttackDeclare 异常: {ex.Message}"); }
    }

    private async Task AdvanceBattleAfterBlockAsync()
    {
        if (State.CurrentBattle is null) return;
        var defenderIdx = State.CurrentBattle.DefenderPlayerIndex;
        var def = State.Players[defenderIdx];
        bool canCounter = def.Hand.Any(c => c.Info.Counter > 0 || c.Info.EffectText.Contains("【反击】"));
        if (!canCounter)
        {
            BattleEngine.PassCounter(State);
            Broadcast("ResolveBattle");
            await ResolveBattleDamageAsync(defenderIdx);
        }
    }

    private void HandleDeclareBlocker(int playerIndex, JsonElement data)
    {
        if (!data.TryGetProperty("blockerId", out var bid)) { SendError(playerIndex, "缺少 blockerId"); return; }
        if (!Guid.TryParse(bid.GetString() ?? "", out var blockerId)) { SendError(playerIndex, "blockerId 非法"); return; }
        var v = ActionValidator.CanDeclareBlocker(State, playerIndex, blockerId);
        if (!v.Ok) { SendError(playerIndex, v.Reason!); return; }
        BattleEngine.DeclareBlocker(State, blockerId);
        Broadcast("DeclareBlocker", new { blocker = blockerId.ToString() });
        _ = AdvanceBattleAfterDeclareBlockerAsync();
    }

    private async Task AdvanceBattleAfterDeclareBlockerAsync()
    {
        try
        {
            await BattleEngine.TriggerBlockDeclareAsync(State, Prompts);
            if (State.IsGameOver || State.CurrentBattle is null) { CheckGameOver(); return; }
            await AdvanceBattleAfterBlockAsync();
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Battle] BlockDeclare 异常: {ex.Message}"); }
    }

    private void HandlePassBlock(int playerIndex)
    {
        if (State.CurrentBattle is null) { SendError(playerIndex, "无战斗"); return; }
        if (State.Phase != Phase.BattleBlock) { SendError(playerIndex, "不在阻挡步骤"); return; }
        if (State.CurrentBattle.DefenderPlayerIndex != playerIndex) { SendError(playerIndex, "不是防守方"); return; }
        BattleEngine.PassBlock(State);
        Broadcast("PassBlock");
        _ = AdvanceBattleAfterBlockAsync();
    }

    private void HandlePlayCounter(int playerIndex, JsonElement data)
    {
        if (State.CurrentBattle is null || State.Phase != Phase.BattleCounter)
        { SendError(playerIndex, "不在反击步骤"); return; }
        if (State.CurrentBattle.DefenderPlayerIndex != playerIndex)
        { SendError(playerIndex, "不是防守方"); return; }

        var def = State.Players[playerIndex];
        bool useCounterIcon = data.TryGetProperty("useCounterIcon", out var uci) && uci.ValueKind == JsonValueKind.True;

        if (useCounterIcon)
        {
            // 从手牌选一张有 counter 值的角色卡，丢入废弃区，并给当前被攻击目标加力量
            if (!data.TryGetProperty("handIndex", out var hi) || hi.ValueKind != JsonValueKind.Number)
            { SendError(playerIndex, "缺少 handIndex"); return; }
            int handIndex = hi.GetInt32();
            if (handIndex < 0 || handIndex >= def.Hand.Count) { SendError(playerIndex, "手牌索引非法"); return; }
            var counterCard = def.Hand[handIndex];
            if (counterCard.Info.Counter <= 0) { SendError(playerIndex, "该卡无反击值"); return; }
            def.Hand.RemoveAt(handIndex);
            def.Trash.Add(counterCard);
            BattleEngine.ApplyCounter(State, playerIndex, counterCard.Info.Counter);
            Broadcast("CounterIcon", new { handIndex, value = counterCard.Info.Counter });
        }
        else
        {
            // 反击事件：M3 完整接入；M2 暂跳过
            SendError(playerIndex, "反击事件 M3 实现");
        }
    }

    private void HandlePassCounter(int playerIndex)
    {
        if (State.CurrentBattle is null || State.Phase != Phase.BattleCounter)
        { SendError(playerIndex, "不在反击步骤"); return; }
        if (State.CurrentBattle.DefenderPlayerIndex != playerIndex)
        { SendError(playerIndex, "不是防守方"); return; }
        int defenderIdx = State.CurrentBattle.DefenderPlayerIndex;
        BattleEngine.PassCounter(State);
        Broadcast("ResolveBattle");
        _ = ResolveBattleDamageAsync(defenderIdx);
    }

    /// <summary>异步伤害结算：BattleEngine.ResolveDamageAsync（含 PreKO 拦截）+ 生命牌触发 + EndBattle</summary>
    private async Task ResolveBattleDamageAsync(int defenderIdx)
    {
        try
        {
            int leaderDamage = await BattleEngine.ResolveDamageAsync(State, Prompts);
            if (leaderDamage > 0 && defenderIdx >= 0)
                await LifeRevealManager.DealDamageToLeader(this, defenderIdx, leaderDamage);
            BattleEngine.EndBattle(State);
            Broadcast("BattleEnd");
            CheckGameOver();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Battle] 异步伤害处理异常: {ex.Message}");
        }
    }

    private void CheckGameOver()
    {
        if (State.IsGameOver)
            Broadcast("DuelOver", new { winner = State.WinnerIndex, reason = State.GameOverReason });
    }

    public void BroadcastInitialState()
    {
        State.Tick++;
        // 给双方各自发一份脱敏快照
        OnSendToPlayer?.Invoke(0, StateSnapshotBuilder.Build(State, 0, "GameStart"));
        OnSendToPlayer?.Invoke(1, StateSnapshotBuilder.Build(State, 1, "GameStart"));
        var publicSnapshot = StateSnapshotBuilder.Build(State, -1, "GameStart");
        OnSendToSpectators?.Invoke(-1, publicSnapshot);
        OnReplay?.Invoke(new { kind = "state", tick = State.Tick, snapshot = publicSnapshot });
        RecordMatchLog("public_snapshot", -1, publicSnapshot);
        RecordMatchLog("private_snapshot", -1, PrivateStateSnapshotBuilder.Build(State));
    }

    public void Broadcast(string lastAction, object? payload = null)
    {
        State.Tick++;
        OnSendToPlayer?.Invoke(0, StateSnapshotBuilder.Build(State, 0, lastAction, payload));
        OnSendToPlayer?.Invoke(1, StateSnapshotBuilder.Build(State, 1, lastAction, payload));
        var publicSnapshot = StateSnapshotBuilder.Build(State, -1, lastAction, payload);
        OnSendToSpectators?.Invoke(-1, publicSnapshot);
        OnReplay?.Invoke(new { kind = "state", tick = State.Tick, lastAction, payload, snapshot = publicSnapshot });
        RecordMatchLog("public_snapshot", -1, publicSnapshot);
        RecordMatchLog("private_snapshot", -1, PrivateStateSnapshotBuilder.Build(State));
    }

    private void SendError(int playerIndex, string reason)
    {
        _activeActionRejected = true;
        RecordMatchLog("player_action_rejected", _activeActor ?? playerIndex, new
        {
            action = _activeAction ?? "",
            reason,
        });
        OnSendToPlayer?.Invoke(playerIndex, new { proto = "MsgActionRejected", reason });
    }

    public void RecordMatchLog(string kind, int? actor, object? payload)
    {
        if (OnMatchLog is null)
        {
            _pendingMatchLogs.Add((kind, actor, payload));
            return;
        }
        OnMatchLog.Invoke(kind, actor, payload);
    }

    public void FlushPendingMatchLogs()
    {
        if (OnMatchLog is null || _pendingMatchLogs.Count == 0) return;
        foreach (var (kind, actor, payload) in _pendingMatchLogs)
            OnMatchLog.Invoke(kind, actor, payload);
        _pendingMatchLogs.Clear();
    }

    // ── Mulligan ─────────────────────────────────────────────────────────

    private void HandleMulligan(int playerIndex, JsonElement data)
    {
        var p = State.Players[playerIndex];
        if (p.MulliganDone)
        {
            SendError(playerIndex, "已完成换牌");
            return;
        }
        bool redraw = false;
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("redraw", out var r))
            redraw = r.ValueKind == JsonValueKind.True;

        if (redraw && p.HasReDraw)
        {
            // 把当前 5 张手牌放回卡组顶部 → 洗牌 → 重抽 5 张
            var hand = new List<CardInstance>(p.Hand);
            p.Hand.Clear();
            p.Deck.AddRange(hand);
            ShuffleDeck(p, playerIndex, "mulligan_redraw");
            for (int i = 0; i < 5 && p.Deck.Count > 0; i++)
            {
                var top = p.Deck[0];
                p.Deck.RemoveAt(0);
                p.Hand.Add(top);
            }
            p.HasReDraw = false;
        }
        p.MulliganDone = true;

        if (State.MulliganBothDone)
        {
            TurnEngine.StartFirstTurn(State);
            Broadcast("MulliganComplete");
        }
        else
        {
            Broadcast("MulliganUpdate");
        }
    }

    // ── Use Effect（启动主要） ────────────────────────────────────────────

    private void HandleUseEffect(int playerIndex, JsonElement data)
    {
        if (!data.TryGetProperty("sourceId", out var sid) || sid.ValueKind != JsonValueKind.String)
        { SendError(playerIndex, "缺少 sourceId"); return; }
        if (!Guid.TryParse(sid.GetString(), out var sourceId)) { SendError(playerIndex, "sourceId 非法"); return; }

        var v = ActionValidator.CanUseEffect(State, playerIndex, sourceId);
        if (!v.Ok) { SendError(playerIndex, v.Reason!); return; }

        var me = State.Players[playerIndex];
        CardInstance? source = me.Leader.Id == sourceId ? me.Leader
            : me.Characters.FirstOrDefault(c => c.Id == sourceId)
              ?? (me.StageCard?.Id == sourceId ? me.StageCard : null);
        if (source is null) { SendError(playerIndex, "效果来源不存在"); return; }

        _ = ResolveActivatedAsync(playerIndex, source);
    }

    private async Task ResolveActivatedAsync(int playerIndex, CardInstance source)
    {
        try
        {
            await EffectRuntime.Resolve(State, playerIndex, source, EffectTrigger.ActivatedMain, Prompts);
            Broadcast("UseEffect", new { source = source.Id.ToString(), card = source.Info.Number });
            CheckGameOver();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UseEffect] {source.Info.Number} 异常: {ex.Message}");
        }
    }

    // ── Prompt Response ──────────────────────────────────────────────────

    private void HandlePromptResponse(int playerIndex, JsonElement data)
    {
        if (State.PendingPrompt is null) { SendError(playerIndex, "没有待响应的 prompt"); return; }
        if (State.PendingPrompt.PlayerIndex != playerIndex) { SendError(playerIndex, "不是你的 prompt"); return; }
        var promptId = data.TryGetProperty("promptId", out var pi) ? pi.GetString() ?? "" : "";
        if (promptId != State.PendingPrompt.PromptId) { SendError(playerIndex, "promptId 不匹配"); return; }
        var chosen = new List<string>();
        if (data.TryGetProperty("chosen", out var ch) && ch.ValueKind == JsonValueKind.Array)
            foreach (var item in ch.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                    chosen.Add(item.GetString()!);
        Prompts.Resolve(promptId, chosen);
    }

    // ── End Turn ─────────────────────────────────────────────────────────

    private void HandleEndTurn(int playerIndex)
    {
        if (State.CurrentTurnPlayer != playerIndex)
        {
            SendError(playerIndex, "不是你的回合");
            return;
        }
        if (State.Phase != Phase.Main)
        {
            SendError(playerIndex, "只能在主要阶段结束回合");
            return;
        }
        TurnEngine.AdvanceTurn(State);
        Broadcast("EndTurn", new { newTurnPlayer = State.CurrentTurnPlayer, turnCount = State.TurnCount });
    }

    // ── Surrender ────────────────────────────────────────────────────────

    private void HandleSurrender(int playerIndex)
    {
        State.WinnerIndex = 1 - playerIndex;
        State.GameOverReason = $"{State.Players[playerIndex].AccountName} 投降";
        Broadcast("Surrender", new { surrendered = playerIndex });
    }

    // ── 初始化辅助 ────────────────────────────────────────────────────────

    public static IReadOnlyList<CardInstance> ParseDeck(string deckRaw, out CardInfo leader)
    {
        var lines = deckRaw.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        var leaderInfo = CardDatabase.Get(lines[0]) ?? throw new Exception($"领航不存在: {lines[0]}");
        leader = leaderInfo;
        var list = new List<CardInstance>();
        foreach (var n in lines.Skip(1))
        {
            var info = CardDatabase.Get(n) ?? throw new Exception($"卡牌不存在: {n}");
            list.Add(new CardInstance { Info = info });
        }
        return list;
    }

    private static void InitDonDeck(PlayerState p)
    {
        // 10 张咚（用 placeholder Info：name="咚"）
        // 我们不在 CardDatabase 中放咚，单独用 DonCard 类型表示
        for (int i = 0; i < 10; i++)
            p.DonDeck.Add(new DonCard());
    }

    private void InitLifeAndHand(PlayerState p, int playerIndex)
    {
        ShuffleDeck(p, playerIndex, "initial_setup");
        // 生命数 = 领航 cost 字段
        int lifeCount = p.Leader.Info.Cost > 0 ? p.Leader.Info.Cost : 5;
        for (int i = 0; i < lifeCount && p.Deck.Count > 0; i++)
        {
            var top = p.Deck[0]; p.Deck.RemoveAt(0);
            p.LifeArea.Add(top);
        }
        // 抽 5 张起手
        for (int i = 0; i < 5 && p.Deck.Count > 0; i++)
        {
            var top = p.Deck[0]; p.Deck.RemoveAt(0);
            p.Hand.Add(top);
        }
    }

    public void ShuffleDeck(PlayerState player, int playerIndex, string reason)
    {
        var before = player.Deck.Select(SnapshotRandomCard).ToArray();
        Shuffle(player.Deck);
        var after = player.Deck.Select(SnapshotRandomCard).ToArray();
        var randomSeq = ++State.RandomSeq;
        RecordMatchLog("random_event", playerIndex, new
        {
            randomSeq,
            type = "shuffle",
            zone = "deck",
            reason,
            playerIndex,
            rngSeed = State.RngSeed,
            count = player.Deck.Count,
            beforeOrder = before,
            afterOrder = after,
        });
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static object SnapshotRandomCard(CardInstance card)
        => new { id = card.Id.ToString(), number = card.Info.Number };
}
