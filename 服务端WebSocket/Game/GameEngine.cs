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
    public Action<int, string, JsonElement>? OnPersistAction { get; set; } // 被接受动作持久化（重启恢复用）
    private string? _activeAction;
    private int? _activeActor;
    private bool _activeActionRejected;
    private readonly List<(string Kind, int? Actor, object? Payload)> _pendingMatchLogs = new();

    /// <summary>本局确定性卡实例 ID 工厂（由 RngSeed 派生，全局唯一计数器）。重放重建依赖它。</summary>
    private readonly Func<Guid> _idFactory;

    /// <summary>最近一次动作触发的最外层 fire-and-forget 效果链；重放在喂下一个动作前等它进入稳定态。</summary>
    private Task _settle = Task.CompletedTask;
    private Task Track(Task t) { _settle = t; return t; }

    /// <summary>
    /// 用双方 deck 字符串构造引擎（已通过 DeckValidator 校验）
    /// firstPlayer = 先手玩家索引 (0/1)
    /// </summary>
    public GameEngine(string roomId, (string sessionId, string accountName, string deckRaw) p0,
                                       (string sessionId, string accountName, string deckRaw) p1,
                                       int firstPlayer,
                                       int? rngSeed = null,
                                       bool leaderKeywordWildcard = false)
    {
        var seed = rngSeed ?? RandomNumberGenerator.GetInt32(int.MaxValue);
        // 必须在创建任何卡实例（ParseDeck/InitDonDeck/InitLifeAndHand）之前装好确定性 ID 工厂，
        // 否则建出的牌走随机 GUID，重放将无法对齐。
        _idFactory = DeterministicId.SeededFactory(seed);
        DeterministicId.Current = _idFactory;
        State = new GameState { RoomId = roomId, FirstPlayer = firstPlayer, RngSeed = seed };

        var p0Cards = ParseDeck(p0.deckRaw, out var p0Leader);
        var p1Cards = ParseDeck(p1.deckRaw, out var p1Leader);

        var player0 = new PlayerState
        {
            SessionId   = p0.sessionId,
            AccountName = p0.accountName,
            Leader      = new CardInstance { Info = leaderKeywordWildcard ? p0Leader.WithWildcardKeywords() : p0Leader },
        };
        player0.Deck.AddRange(p0Cards);
        InitDonDeck(player0);
        InitLifeAndHand(player0, 0);

        var player1 = new PlayerState
        {
            SessionId   = p1.sessionId,
            AccountName = p1.accountName,
            Leader      = new CardInstance { Info = leaderKeywordWildcard ? p1Leader.WithWildcardKeywords() : p1Leader },
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

        // 动作可能由新线程（重连/线程池）进入，重新挂上本局确定性 ID 工厂；
        // 由本动作启动的 async 续延会捕获该上下文并沿用同一计数器。
        DeterministicId.Current = _idFactory;

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
            case "DebugAddCard":   HandleDebugAddCard(playerIndex, data); break;
            case "DebugAddDon":    HandleDebugAddDon(playerIndex, data); break;
            case "DebugRefreshDon": HandleDebugRefreshDon(playerIndex); break;
            case "DebugSummon":    _ = HandleDebugSummonAsync(playerIndex, data); break;
            case "DebugKoAll":     _ = HandleDebugKoAllAsync(playerIndex, data); break;
            case "DebugRestAll":   HandleDebugRestAll(playerIndex, data); break;
            case "DebugLeaderAttack": _ = HandleDebugLeaderAttackAsync(playerIndex); break;
            default:
                SendError(playerIndex, $"未知动作: {action}");
                break;
        }

        if (!_activeActionRejected)
        {
            RecordMatchLog("player_action_accepted", playerIndex, new { action });
            OnPersistAction?.Invoke(playerIndex, action, data); // 仅持久化被接受的动作
        }

        _activeAction = null;
        _activeActor = null;
    }

    /// <summary>
    /// 等待最近一次动作触发的最外层异步效果链进入"稳定态"：要么彻底解析完，要么挂起在
    /// 等待玩家响应的 PendingPrompt 上。供重放/恢复在喂下一个动作前调用，确保不会在效果
    /// 还没跑到稳定点时就塞入后续动作。timeoutMs 仅为防卡死的安全网，不参与对局逻辑。
    /// </summary>
    public async Task WaitSettledAsync(int timeoutMs = 15000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (State.PendingPrompt is null && !_settle.IsCompleted)
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("WaitSettledAsync 超时：效果链未在限定时间内进入稳定态");
            await Task.WhenAny(_settle, Task.Delay(5));
        }
    }

    // ── GM 调试：按编号加牌到手牌 ──────────────────────────────────────────

    private void HandleDebugAddCard(int playerIndex, JsonElement data)
    {
        if (!data.TryGetProperty("cardNumber", out var cn) || cn.ValueKind != JsonValueKind.String)
        { SendError(playerIndex, "缺少 cardNumber"); return; }
        var number = cn.GetString()!.Trim();

        var info = CardDatabase.Get(number);
        if (info == null) { SendError(playerIndex, $"未知卡牌编号: {number}"); return; }

        var card = new CardInstance { Info = info };
        State.Players[playerIndex].Hand.Add(card);
        Broadcast("DebugAddCard", new { player = playerIndex, cardNumber = number });
    }

    // ── GM 调试：加咚（活跃）到费用区 ──────────────────────────────────────
    private void HandleDebugAddDon(int playerIndex, JsonElement data)
    {
        int count = data.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 1;
        if (count <= 0) count = 1;

        var p = State.Players[playerIndex];
        for (int i = 0; i < count; i++)
        {
            DonCard d;
            if (p.DonDeck.Count > 0)
            {
                d = p.DonDeck[0];
                p.DonDeck.RemoveAt(0);
            }
            else
            {
                d = new DonCard(); // 咚卡组已空，GM 直接补一张
            }
            d.State = DonState.Active;
            d.AttachedToCardId = null;
            p.CostArea.Add(d);
        }
        Broadcast("DebugAddDon", new { player = playerIndex, count });
    }

    // ── GM 调试：刷新所有咚（回费用区并转为活跃/竖直，含解除赋予） ──────────
    private void HandleDebugRefreshDon(int playerIndex)
    {
        var p = State.Players[playerIndex];
        foreach (var d in p.CostArea)
        {
            d.State = DonState.Active;
            d.AttachedToCardId = null;
        }
        Broadcast("DebugRefreshDon", new { player = playerIndex });
    }

    // ── GM 调试：按编号直接召唤到场上（不扣费，但触发登场效果，贴近真实登场） ──────────────
    // 触发 OnEnterField 是为了让"靠登场注册持续效果"的卡（如 OP16-017 条件减力、OP16-003 持续增益）
    // 在调试召唤下也能正确生效；代价是带【登场时】prompt 的卡会弹出选择，非纯净摆场。
    private async Task HandleDebugSummonAsync(int playerIndex, JsonElement data)
    {
        if (!data.TryGetProperty("cardNumber", out var cn) || cn.ValueKind != JsonValueKind.String)
        { SendError(playerIndex, "缺少 cardNumber"); return; }
        var number = cn.GetString()!.Trim();

        var info = CardDatabase.Get(number);
        if (info == null) { SendError(playerIndex, $"未知卡牌编号: {number}"); return; }

        // 召唤目标：self=自己场上，opponent=对手场上，缺省按 self
        var target = data.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : "self";
        var targetIndex = target == "opponent" ? 1 - playerIndex : playerIndex;

        var p = State.Players[targetIndex];
        var card = new CardInstance { Info = info };

        switch (info.Kind)
        {
            case CardKind.Character:
                if (p.Characters.Count >= 5)
                {
                    var sacrifice = p.Characters[0];
                    p.Characters.RemoveAt(0);
                    p.Trash.Add(sacrifice);
                }
                card.TurnPlayed = State.TurnCount; // 沿用登场回合规则（当回合默认不能攻击）
                p.Characters.Add(card);
                break;

            case CardKind.Stage:
                if (p.StageCard is not null)
                {
                    p.Trash.Add(p.StageCard);
                    p.StageCard = null;
                }
                p.StageCard = card;
                break;

            default:
                SendError(playerIndex, $"{number} 是{info.Kind}，不能登场到场上");
                return;
        }
        Broadcast("DebugSummon", new { player = playerIndex, target = targetIndex, cardNumber = number, kind = info.Kind.ToString() });

        // GM「打出到场上」：不扣费，但模拟真实打出——仅对【己方】打出的卡触发【登场时】效果
        //（持续效果型卡如 OP16-017 也借此注册其 ContinuousEffect）。
        // 摆到对手场仅作布置，不触发其登场效果，避免对手卡效果在调试布置时反向干扰己方。
        if ((info.Kind == CardKind.Character || info.Kind == CardKind.Stage) && targetIndex == playerIndex)
        {
            try
            {
                await EffectRuntime.Resolve(State, targetIndex, card, EffectTrigger.OnEnterField, Prompts);
            }
            catch (Exception ex) { Console.Error.WriteLine($"[GM] 登场效果结算异常: {ex.Message}"); }
            // 效果链（含连续 prompt）结算完成后必须再广播一次最终状态，否则以 prompt 结尾的
            // 登场效果（如 OP16-026 的 PlayCharFromHand）结算后客户端 PendingPrompt 不会被清空 → 卡住。
            Broadcast("EffectResolved", new { cardNumber = number });
        }
    }

    // ── GM 调试：KO 指定一方场上全部角色（走完整 KO 流程，触发【K.O.时】等效果） ──
    private async Task HandleDebugKoAllAsync(int playerIndex, JsonElement data)
    {
        try
        {
            // KO 目标：self=自己场上，opponent=对手场上，缺省按 self
            var target = data.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : "self";
            var targetIndex = target == "opponent" ? 1 - playerIndex : playerIndex;

            var p = State.Players[targetIndex];
            var victims = p.Characters.ToList(); // 复制：KO 过程会改动 Characters
            int count = victims.Count;

            // 仅 KO 角色，不动 Leader/舞台；逐个走异步 KO 流程（触发 PreKO/置换/OnKO）。
            // GM 模拟"因对方效果被KO"：设置来源标记，使 OP16-024 等【因对方的效果被KO时】效果发动。
            // 仍用 KOCardAsync（非 KOByEffectAsync）：GM 不在效果上下文内，前者用 TriggerEvent 立即派发
            // OnAnyCharKOd，后者用 NotifyWatcher 会因无 ambient 被丢弃，反而破坏其它卡的 KO 联动。
            State.KOReason = "effect";
            State.KOActingSide = 1 - targetIndex;   // KO 方=被KO一方的对手
            try
            {
                foreach (var card in victims)
                {
                    if (State.IsGameOver) break;
                    await BattleEngine.KOCardAsync(State, targetIndex, card, Prompts);
                }
            }
            finally
            {
                State.KOReason = null;
                State.KOActingSide = -1;
            }

            Broadcast("DebugKoAll", new { player = playerIndex, target = targetIndex, count });
        }
        catch (Exception ex) { Console.Error.WriteLine($"[GM] KO 全部角色异常: {ex.Message}"); }
    }

    // ── GM 调试：横置指定一方场上全部角色（纯状态变更，不触发横置相关效果）──
    private void HandleDebugRestAll(int playerIndex, JsonElement data)
    {
        // 横置目标：self=自己场上，opponent=对手场上，缺省按 self
        var target = data.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : "self";
        var targetIndex = target == "opponent" ? 1 - playerIndex : playerIndex;

        var p = State.Players[targetIndex];
        int count = 0;
        foreach (var c in p.Characters)
        {
            if (!c.IsTapped) { c.IsTapped = true; count++; }
        }
        Broadcast("DebugRestAll", new { player = playerIndex, target = targetIndex, count });
    }

    // ── GM 调试：对手领袖向我方领袖发起一次完整攻击（走真实战斗流程：阻挡/反击/伤害结算）──
    private async Task HandleDebugLeaderAttackAsync(int playerIndex)
    {
        try
        {
            if (State.CurrentBattle is not null) { SendError(playerIndex, "当前已有战斗进行中"); return; }
            int attackerIdx = 1 - playerIndex;               // 对手
            var attackerLeader = State.Players[attackerIdx].Leader;
            BattleEngine.StartAttackForced(State, attackerIdx, attackerLeader.Id, targetIsLeader: true, targetId: null);
            Broadcast("Attack", new { attacker = attackerLeader.Id.ToString(), targetIsLeader = true, targetId = (string?)null });
            await AdvanceBattleAfterAttackDeclareAsync(attackerIdx);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[GM] 对手领袖攻击异常: {ex.Message}"); }
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
        var handCard = p.Hand[handIndex];

        // 角色区满员（≥5）：先让玩家选择 1 张己方角色送去废弃区，再登场新角色
        if (handCard.Info.Kind == CardKind.Character && p.Characters.Count >= 5)
        {
            _ = Track(PlayCharacterWithOverflowAsync(playerIndex, handCard));
            return;
        }

        var cardNumber = handCard.Info.Number;
        var result = CardPlayer.Play(State, playerIndex, handIndex);
        Broadcast("PlayCard", new { player = playerIndex, cardNumber, kind = result.Kind.ToString(), cardId = result.Card.Id.ToString() });

        // 触发对应效果
        _ = Track(ResolveEffectAsync(playerIndex, result));
    }

    /// <summary>
    /// 角色区满员（5 张）时的登场流程：让玩家选择 1 张己方现有角色送去废弃区（非 KO，不触发【K.O.时】），
    /// 腾出空位后再正常登场新角色并触发其【登场时】效果。玩家未选择则取消登场（不扣费、不弃牌）。
    /// </summary>
    private async Task PlayCharacterWithOverflowAsync(int playerIndex, CardInstance handCard)
    {
        try
        {
            var p = State.Players[playerIndex];
            var chosen = await Prompts.ChooseCards(playerIndex, "OverflowTrash",
                "角色区已满，请选择 1 张角色送去废弃区",
                p.Characters.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (chosen.Count == 0) { SendError(playerIndex, "未选择废弃角色，取消登场"); return; }

            var victim = p.Characters.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
            if (victim is not null) { p.Characters.Remove(victim); p.Trash.Add(victim); }

            // 重新定位手牌索引（prompt 期间游戏锁定、索引通常不变，仍做健壮性处理）
            int handIndex = p.Hand.IndexOf(handCard);
            if (handIndex < 0) { SendError(playerIndex, "手牌状态已变化，取消登场"); return; }

            var cardNumber = handCard.Info.Number;
            var result = CardPlayer.Play(State, playerIndex, handIndex);
            Broadcast("PlayCard", new { player = playerIndex, cardNumber, kind = result.Kind.ToString(), cardId = result.Card.Id.ToString() });

            await ResolveEffectAsync(playerIndex, result);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Play] 满员登场异常: {ex.Message}"); }
    }

    private async Task ResolveEffectAsync(int playerIndex, PlayResult result)
    {
        try
        {
            if (result.Kind == PlayKind.Character || result.Kind == PlayKind.Stage)
            {
                await EffectRuntime.Resolve(State, playerIndex, result.Card, EffectTrigger.OnEnterField, Prompts);
                // 旁观者：当我方(其它)角色登场时
                if (result.Kind == PlayKind.Character && !State.IsGameOver)
                    await EffectRuntime.TriggerEvent(State, EffectTrigger.OnAllyCharEnter, Prompts,
                        new Dictionary<string, object?> { ["cardId"] = result.Card.Id.ToString(), ["owner"] = playerIndex });
            }
            else if (result.Kind == PlayKind.Event)
            {
                await EffectRuntime.Resolve(State, playerIndex, result.Card, EffectTrigger.EventMain, Prompts);

                // OP15-002 路西：启动主要激活后(每回合1次)，本回合内我方发动原始费用≥3的事件时抽1。
                // armed 状态由 OP15_002_Lucci 脚本写入的 TurnOnceUsed 标记表示；事件费用按印刷费 Info.Cost 判定。
                var lucciPlayer = State.Players[playerIndex];
                if (lucciPlayer.Leader.Info.Number == "OP15-002"
                    && lucciPlayer.TurnOnceUsed.Contains("OP15-002-MainOncePerTurn")
                    && result.Card.Info.Cost >= 3)
                {
                    AtomicOps.Draw(State, playerIndex, 1);
                }

                // 旁观者：当对方发动事件时（监听卡为非出牌方，脚本内自行判定）
                if (!State.IsGameOver)
                    await EffectRuntime.TriggerEvent(State, EffectTrigger.OnOppEventPlayed, Prompts,
                        new Dictionary<string, object?> { ["owner"] = playerIndex });
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
        // 赋予咚!! 时触发（OP02-002 戈普领袖：我方回合赋予咚→对方角色减费）
        _ = Track(ResolveDonAttachedAsync(playerIndex, targetId));
    }

    /// <summary>赋予咚!! 后派发 OnDonAttached（监听卡如 OP02-002 据此发动），随后刷新状态</summary>
    private async Task ResolveDonAttachedAsync(int playerIndex, Guid targetId)
    {
        try
        {
            await EffectRuntime.TriggerEvent(State, EffectTrigger.OnDonAttached, Prompts,
                new Dictionary<string, object?> { ["targetId"] = targetId.ToString(), ["owner"] = playerIndex });
            Broadcast("EffectResolved", new { cardNumber = "OnDonAttached" });
            CheckGameOver();
        }
        catch (Exception ex) { Console.Error.WriteLine($"[DonAttached] 派发异常: {ex.Message}"); }
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

        // 攻击前置弃牌税（OP08-043：对方所有角色攻击前须弃 N 张手牌）
        int tax = State.AttackTaxDiscard[playerIndex];
        if (tax > 0)
        {
            if (State.Players[playerIndex].Hand.Count < tax)
            { SendError(playerIndex, $"攻击需弃 {tax} 张手牌，手牌不足"); return; }
            _ = Track(AttackWithTaxAsync(playerIndex, attackerId, targetIsLeader, targetId, tax, attackerStr));
            return;
        }

        BattleEngine.StartAttack(State, attackerId, targetIsLeader, targetId);
        Broadcast("Attack", new { attacker = attackerStr, targetIsLeader, targetId = targetId?.ToString() });

        // 异步推进战斗：触发【攻击时】→ 判断 Block → 判断 Counter → 伤害结算
        _ = Track(AdvanceBattleAfterAttackDeclareAsync(playerIndex));
    }

    private async Task AttackWithTaxAsync(int playerIndex, Guid attackerId, bool targetIsLeader, Guid? targetId, int tax, string attackerStr)
    {
        try
        {
            var me = State.Players[playerIndex];
            var chosen = await Prompts.ChooseCards(playerIndex, "AttackTaxDiscard",
                $"攻击前须丢弃 {tax} 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), tax, tax);
            if (chosen.Count < tax) { for (int i = 0; i < tax && me.Hand.Count > 0; i++) AtomicOps.DiscardHand(me, me.Hand[0]); }
            else foreach (var cid in chosen) { var c = me.Hand.FirstOrDefault(x => x.Id.ToString() == cid); if (c is not null) AtomicOps.DiscardHand(me, c); }

            BattleEngine.StartAttack(State, attackerId, targetIsLeader, targetId);
            Broadcast("Attack", new { attacker = attackerStr, targetIsLeader, targetId = targetId?.ToString() });
            await AdvanceBattleAfterAttackDeclareAsync(playerIndex);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Battle] 攻击税异常: {ex.Message}"); }
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
            bool attackerUnblockable = attackerCard is not null && ActionValidator.HasKeyword(State, attackerCard, "不可阻挡");
            bool hasBlocker = !attackerUnblockable && def.Characters.Any(c => !c.IsTapped && ActionValidator.HasKeyword(State, c, "阻挡者"));
            if (!hasBlocker)
            {
                BattleEngine.PassBlock(State);
                Broadcast("AutoPassBlock");
                await AdvanceBattleAfterBlockAsync();
            }
            else
            {
                // 进入阻挡等待：通知防守方（人类显示阻挡UI / 机器人据此放弃阻挡）
                Broadcast("AwaitBlock");
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Battle] AttackDeclare 异常: {ex.Message}"); }
    }

    private async Task AdvanceBattleAfterBlockAsync()
    {
        if (State.CurrentBattle is null) return;
        var defenderIdx = State.CurrentBattle.DefenderPlayerIndex;
        var def = State.Players[defenderIdx];
        bool canCounter = def.Hand.Any(c => c.Info.Counter > 0 || Array.IndexOf(c.Info.EffectTags, "EventCounter") >= 0);
        if (!canCounter)
        {
            BattleEngine.PassCounter(State);
            Broadcast("ResolveBattle");
            await ResolveBattleDamageAsync(defenderIdx);
        }
        else
        {
            // 进入反击等待：通知防守方（人类显示反击UI / 机器人据此放弃反击）
            Broadcast("AwaitCounter");
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
        _ = Track(AdvanceBattleAfterDeclareBlockerAsync());
    }

    private async Task AdvanceBattleAfterDeclareBlockerAsync()
    {
        try
        {
            await BattleEngine.TriggerBlockDeclareAsync(State, Prompts);
            if (State.IsGameOver || State.CurrentBattle is null) { CheckGameOver(); return; }
            // 旁观者：当对方发动【阻挡者】时（监听卡为攻击方，脚本内判 blockerOwner != self）
            int blockerOwner = State.CurrentBattle.DefenderPlayerIndex;
            await EffectRuntime.TriggerEvent(State, EffectTrigger.OnOppBlocker, Prompts,
                new Dictionary<string, object?> { ["blockerOwner"] = blockerOwner });
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
        _ = Track(AdvanceBattleAfterBlockAsync());
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
            // 反击事件：从手牌打出（通用机制）——校验后异步结算
            if (!data.TryGetProperty("handIndex", out var hi) || hi.ValueKind != JsonValueKind.Number)
            { SendError(playerIndex, "缺少 handIndex"); return; }
            int handIndex = hi.GetInt32();
            if (handIndex < 0 || handIndex >= def.Hand.Count) { SendError(playerIndex, "手牌索引非法"); return; }
            var card = def.Hand[handIndex];
            if (System.Array.IndexOf(card.Info.EffectTags, "EventCounter") < 0)
            { SendError(playerIndex, "该卡没有反击效果"); return; }
            int cost = State.HandPlayCost(playerIndex, card);
            if (def.ActiveDonCount < cost) { SendError(playerIndex, "活跃咚不足，无法打出该反击事件"); return; }
            _ = Track(PlayCounterEventAsync(playerIndex, handIndex));
        }
    }

    /// <summary>反击事件结算：扣费+移入废弃区 → 触发 EventCounter → 仍停在反击步骤，可继续打反击或 Pass。</summary>
    private async Task PlayCounterEventAsync(int playerIndex, int handIndex)
    {
        try
        {
            var def = State.Players[playerIndex];
            if (handIndex < 0 || handIndex >= def.Hand.Count) return;
            var cardNumber = def.Hand[handIndex].Info.Number;
            var result = CardPlayer.Play(State, playerIndex, handIndex);   // 复用：按 HandPlayCost 扣活跃咚→休息，事件入废弃区
            Broadcast("PlayCard", new { player = playerIndex, cardNumber, kind = result.Kind.ToString(), cardId = result.Card.Id.ToString() });
            await EffectRuntime.Resolve(State, playerIndex, result.Card, EffectTrigger.EventCounter, Prompts);
            Broadcast("EffectResolved", new { cardNumber });
            if (State.IsGameOver || State.CurrentBattle is null) { CheckGameOver(); return; }
            Broadcast("AwaitCounter");   // 仍在反击步骤，等待继续反击或 PassCounter
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Battle] 反击事件异常: {ex.Message}"); }
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
        _ = Track(ResolveBattleDamageAsync(defenderIdx));
    }

    /// <summary>异步伤害结算：BattleEngine.ResolveDamageAsync（含 PreKO 拦截）+ 生命牌触发 + EndBattle</summary>
    private async Task ResolveBattleDamageAsync(int defenderIdx)
    {
        try
        {
            int leaderDamage = await BattleEngine.ResolveDamageAsync(State, Prompts);
            if (leaderDamage > 0 && defenderIdx >= 0)
                await LifeRevealManager.DealDamageToLeader(this, defenderIdx, leaderDamage);
            // 【战斗结束时】触发：在 EndBattle 清除 CurrentBattle 前派发（监听卡可读 CurrentBattle 判断是否参战）
            if (!State.IsGameOver && State.CurrentBattle is { } bend)
            {
                await EffectRuntime.TriggerEvent(State, EffectTrigger.OnBattleEnd, Prompts,
                    new Dictionary<string, object?>
                    {
                        ["attackerId"] = bend.AttackerCardId.ToString(),
                        ["attackerPlayerIdx"] = bend.AttackerPlayerIndex,
                        ["defenderPlayerIdx"] = bend.DefenderPlayerIndex,
                        ["targetIsLeader"] = bend.TargetIsLeader,
                        ["targetCardId"] = (bend.ReplacedByBlockerCardId ?? bend.TargetCardId)?.ToString(),
                    });
            }
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

    /// <summary>短暂向双方公开 ownerIndex 检索到的牌（搭一次广播即清空），客户端弹出展示浮层</summary>
    public void BroadcastReveal(int ownerIndex, IReadOnlyList<string> cardNumbers)
    {
        if (cardNumbers.Count == 0) return;
        State.PendingReveal = new RevealInfo { OwnerIndex = ownerIndex, CardNumbers = cardNumbers.ToList() };
        Broadcast("RevealCards", new { player = ownerIndex });
        State.PendingReveal = null;
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
            // 注册双方领袖的永续被动（如 OP16-080【对方回合中】我方角色费用+1）。
            // 注册为纯状态写入（无 prompt），同步完成后再广播，使快照立即包含该效果。
            RegisterLeaderPassives();
            Broadcast("MulliganComplete");
        }
        else
        {
            Broadcast("MulliganUpdate");
        }
    }

    /// <summary>
    /// 开局注册双方领袖的永续被动（OnGameStart）。领袖被动应为纯状态写入、不触发 prompt，
    /// 故 Resolve 同步跑完即完成注册；OnGameStart 对无此被动的领袖（含纯 DSL 卡）为 no-op。
    /// </summary>
    private void RegisterLeaderPassives()
    {
        for (int i = 0; i < 2; i++)
            _ = Track(EffectRuntime.Resolve(State, i, State.Players[i].Leader, EffectTrigger.OnGameStart, Prompts));
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

        _ = Track(ResolveActivatedAsync(playerIndex, source));
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
        _ = Track(EndTurnAsync());
    }

    /// <summary>结束阶段：先派发【我方的回合结束时】（当前回合方）与【对方的回合结束时】（对方）效果，
    /// 可选效果由各卡脚本内 ConfirmOptional 提示；随后清理回合状态并切到对方回合。
    /// 注：此前 EnterEndPhase 从不派发回合结束事件 → 所有 OnMyTurnEnd/OnOppTurnEnd 效果失效（反馈#43）。</summary>
    private async Task EndTurnAsync()
    {
        try
        {
            State.Phase = Phase.End;
            int cur = State.CurrentTurnPlayer;
            await EffectRuntime.TriggerEvent(State, EffectTrigger.OnMyTurnEnd, Prompts,
                new Dictionary<string, object?> { ["owner"] = cur });
            if (!State.IsGameOver)
                await EffectRuntime.TriggerEvent(State, EffectTrigger.OnOppTurnEnd, Prompts,
                    new Dictionary<string, object?> { ["owner"] = 1 - cur });
            if (State.IsGameOver) { CheckGameOver(); return; }

            TurnEngine.AdvanceTurnToReset(State);
            // 【我方的回合开始时】(OP11-040 路飞等)：在准备阶段(Reset)之后、抽牌/加咚之前派发。
            // 此刻费用区咚数 = 进入本回合的咚总数（本回合 Don 尚未加咚），符合官方「回合开始时」判定时点。
            // 经 isMyTurn 过滤后仅当前回合方的卡触发；payload owner = 新回合方。
            if (!State.IsGameOver)
                await EffectRuntime.TriggerEvent(State, EffectTrigger.OnTurnStart, Prompts,
                    new Dictionary<string, object?> { ["owner"] = State.CurrentTurnPlayer });
            TurnEngine.AdvanceTurnToMain(State);
            // 「下个对方主要阶段开始时」延迟任务（PRB02-005 路飞）：此刻已切到新回合方、咚已 reset 活跃
            RunNextOppMainPhaseTasks();
            Broadcast("EndTurn", new { newTurnPlayer = State.CurrentTurnPlayer, turnCount = State.TurnCount });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[EndTurnAsync] {ex}");
        }
    }

    /// <summary>「下个对方主要阶段开始时」延迟任务（PRB02-005 路飞）：AdvanceTurn 切到新回合后调用。
    /// 登记方 Owner 的对方 = 新回合方时执行（Owner == 1 - 新回合方），令新回合方失去 1 张活跃咚的活性。</summary>
    private void RunNextOppMainPhaseTasks()
    {
        if (State.NextOppMainPhaseTasks.Count == 0) return;
        int newTurn = State.CurrentTurnPlayer;
        var due = State.NextOppMainPhaseTasks.Where(t => t.Owner == 1 - newTurn).ToList();
        foreach (var t in due)
        {
            State.NextOppMainPhaseTasks.Remove(t);
            if (t.Kind == "RestOneActiveDon")
            {
                // 对方（登记方的对手 = 新回合方）将其 1 张未被赋予的活跃咚转为休息状态
                var oppP = State.Players[newTurn];
                var don = oppP.CostArea.FirstOrDefault(d => d.State == DonState.Active && d.AttachedToCardId is null);
                if (don is not null) don.State = DonState.Rest;
            }
        }
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
        // 艾尼路(OP15-058)持续规则：我方咚!!卡组变为 6 张
        int donCount = p.Leader.Info.Number == "OP15-058" ? 6 : 10;
        for (int i = 0; i < donCount; i++)
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
            int j = State.Rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static object SnapshotRandomCard(CardInstance card)
        => new { id = card.Id.ToString(), number = card.Info.Number };
}
