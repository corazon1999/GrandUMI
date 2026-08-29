using GrandUMI.Cards;
using GrandUMI.Diagnostics;
using GrandUMI.Effects;
using GrandUMI.Effects.Rules;
using GrandUMI.Game.Debug;
using GrandUMI.Game.Logging;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using GrandUMI.Training;
using System.Security.Cryptography;
using System.Text.Json;

namespace GrandUMI.Game;

internal sealed record AcceptedActionLogReceipt(
    long OrderSeq,
    string StableHash,
    bool Queued);

internal sealed record GameActionExecutionReceipt(
    bool Accepted,
    AcceptedActionLogReceipt? AcceptedLog);

/// <summary>
/// 单个房间的对战引擎（线程不安全；所有调用需在 GameRoomManager 中串行化）
/// </summary>
public class GameEngine
{
    public const int StartingPlayerChoiceTimeoutSeconds = 60;
    public const int MulliganTimeoutSeconds = 60;
    public GameState State { get; }
    public PromptSystem Prompts { get; }
    public Action<int, object>? OnSendToPlayer { get; set; }   // (playerIndex, payload)
    public Action<object>?      OnBroadcast    { get; set; }   // 双方都收到
    public Action<string, int?, object?>? OnMatchLog { get; set; }
    public Func<string, int?, object?, MatchLogAppendReceipt>? OnMatchLogWithReceipt { get; set; }
    public Action<int, object, object?>? OnSendToSpectators { get; set; } // (主视角, 脱敏快照, 可选手牌快照)
    public Func<bool>? HasSpectators { get; set; }
    public Func<int, bool>? HasSpectatorsForPerspective { get; set; }
    public Func<int, bool>? HasSpectatorsWithHandForPerspective { get; set; }
    public Action<int, string, JsonElement, string?>? OnPersistAction { get; set; } // 被接受动作持久化（重启恢复用）
    public Action? OnOpeningSequenceReady { get; set; }
    /// <summary>每次构建权威快照前同步房间级操作棋钟。</summary>
    public Action? BeforeSnapshot { get; set; }
    /// <summary>
    /// 是否记录含双方完整牌库的私有快照。直接构造引擎时默认开启以兼容测试；
    /// 线上房间由 GameRoomManager 按环境变量显式配置，默认关闭。
    /// </summary>
    public bool EnablePrivateSnapshotLog { get; set; } = true;
    private string? _activeAction;
    private int? _activeActor;
    private string? _activeRequestId;
    private GameActionSource _activeActionSource;
    private bool _activeActionRejected;
    private readonly object _snapshotBatchGate = new();
    private bool _snapshotBatchActive;
    private int _trackedOperations;
    private PendingBroadcast? _pendingBroadcast;
    private long _latencyActionStartedAt;
    private string _latencyAction = "";
    private string? _latencyRequestId;
    private readonly List<(string Kind, int? Actor, object? Payload)> _pendingMatchLogs = new();
    private readonly List<ActionLogEvent> _pendingActionLogs = new();
    private readonly List<EffectActivationEvent> _pendingEffectActivations = new();
    private readonly List<StateSnapshotBuilder.ReplayHandFrame> _replayHandTimeline = new();
    /// <summary>
    /// 当前仍在解析 OnDonAttached 的服务端贴咚操作序号。若解析停在 Prompt，只有匹配该序号的
    /// 权威撤回才能取消 Prompt；避免普通效果 Prompt 被越权中止。
    /// </summary>
    private long? _resolvingAttachDonOperationSequence;

    /// <summary>本局确定性卡实例 ID 工厂（由 RngSeed 派生，全局唯一计数器）。重放重建依赖它。</summary>
    private readonly Func<Guid> _idFactory;
    /// <summary>骰点对局是否延迟到先后手确定后再洗牌、设置生命区并抽取起手牌。</summary>
    private readonly bool _deferOpeningSetupUntilFirstPlayerChosen;
    private readonly bool _deferInitialSetupUntilStart;
    private bool _openingSequenceStarted;
    /// <summary>双方领袖的 OnGameStart 是否已在准备起手牌前完成。</summary>
    private bool _leaderStartEffectsResolved;

    /// <summary>最近一次动作触发的最外层 fire-and-forget 效果链；重放在喂下一个动作前等它进入稳定态。</summary>
    private Task _settle = Task.CompletedTask;
    private Task Track(Task task)
    {
        Interlocked.Increment(ref _trackedOperations);
        var tracked = CompleteTrackedAsync(task);
        _settle = tracked;
        return tracked;
    }

    private async Task CompleteTrackedAsync(Task task)
    {
        try { await task; }
        finally
        {
            if (Interlocked.Decrement(ref _trackedOperations) == 0)
                EndSnapshotBatch();
        }
    }
    private bool _op17CoverageRunning;

    /// <summary>
    /// 必须立刻下发的交互/动画屏障。到达屏障时，之前暂存的普通状态会被当前完整快照覆盖；
    /// 屏障之后产生的新状态仍可继续合并到下一个稳定点。
    /// </summary>
    private static readonly HashSet<string> ImmediateSnapshotActions = new(StringComparer.Ordinal)
    {
        "Prompt", "PromptTimeout", "RevealCards",
        "Attack", "AwaitBlock", "AwaitCounter", "DeclareBlocker", "CounterIcon", "PlayCard",
        "UndoAttachDon",
        "FirstPlayerChosen", "MulliganComplete", "MulliganUpdate",
        "DuelOver", "Surrender", "DrawRequested", "DrawRequestRejected", "DrawAgreed",
        "OperationTimeout", "DisconnectTimeout", "PlayerDisconnected", "PlayerReconnected",
        "DebugOP17CoverageStarted", "DebugOP17CoverageResult",
    };

    private sealed record PendingBroadcast(string LastAction, object? Payload);

    /// <summary>
    /// 用双方 deck 字符串构造引擎（已通过 DeckValidator 校验）
    /// firstPlayer = 先手玩家索引 (0/1)；-1 表示启用开局骰点选择流程
    /// deferOpeningSetupUntilFirstPlayerChosen 仅用于新骰点对局；恢复旧日志时保持 false 以兼容旧随机序列。
    /// </summary>
    public GameEngine(string roomId, (string sessionId, string accountName, string deckRaw) p0,
                                       (string sessionId, string accountName, string deckRaw) p1,
                                       int firstPlayer,
                                       int? rngSeed = null,
                                       bool leaderKeywordWildcard = false,
                                       bool deferOpeningSetupUntilFirstPlayerChosen = false,
                                       bool deferInitialSetupUntilStart = false,
                                       CardRuleset? ruleset = null)
    {
        var seed = rngSeed ?? RandomNumberGenerator.GetInt32(int.MaxValue);
        var pinnedRuleset = ruleset ?? CardRulesetManager.Current;
        // 必须在创建任何卡实例（ParseDeck/InitDonDeck/InitLifeAndHand）之前装好确定性 ID 工厂，
        // 否则建出的牌走随机 GUID，重放将无法对齐。
        _idFactory = DeterministicId.SeededFactory(seed);
        _deferOpeningSetupUntilFirstPlayerChosen = deferOpeningSetupUntilFirstPlayerChosen;
        _deferInitialSetupUntilStart = deferInitialSetupUntilStart;
        DeterministicId.Current = _idFactory;
        State = new GameState
        {
            RoomId = roomId,
            FirstPlayer = firstPlayer,
            RngSeed = seed,
            RulesetId = pinnedRuleset.Id,
            Ruleset = pinnedRuleset,
        };

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

        var player1 = new PlayerState
        {
            SessionId   = p1.sessionId,
            AccountName = p1.accountName,
            Leader      = new CardInstance { Info = leaderKeywordWildcard ? p1Leader.WithWildcardKeywords() : p1Leader },
        };
        player1.Deck.AddRange(p1Cards);
        InitDonDeck(player1);

        State.Players[0] = player0;
        State.Players[1] = player1;
        // 预设先后手（如单人测试）以及旧日志恢复沿用构造时发牌；
        // 新骰点对局必须等胜者选定先后手后才生成生命区和起手牌。
        if (!_deferInitialSetupUntilStart
            && (!_deferOpeningSetupUntilFirstPlayerChosen || firstPlayer >= 0))
        {
            InitLifeAndHand(player0, 0);
            InitLifeAndHand(player1, 1);
        }
        State.CurrentTurnPlayer = firstPlayer;
        State.Phase = Phase.Reset;
        State.TurnCount = 0; // 在双方完成 mulligan 后调用 TurnEngine.StartFirstTurn 才进入 turn 1
        if (!_deferInitialSetupUntilStart && firstPlayer < 0)
        {
            State.OpeningStage = OpeningStage.RollingDice;
            RollStartingDice();
            State.OpeningStage = OpeningStage.WaitingFirstPlayerChoice;
            State.StartingPlayerChoiceDeadlineUtc = DateTime.UtcNow.AddSeconds(StartingPlayerChoiceTimeoutSeconds);
        }
        Prompts = new PromptSystem(this);
        if (!_deferInitialSetupUntilStart && State.StartingPlayerChosen)
            BeginMulliganPhase();
        CaptureReplayHands();
    }

    /// <summary>
    /// 线上新对局在首份快照前启动的开局序列。先处理可交互的 OnGameStart，
    /// 再进行骰点或预设先后手的生命/起手牌初始化，避免 OP13-079 被延迟到骰点或调度之后。
    /// </summary>
    public void BeginOpeningSequence()
    {
        if (!_deferInitialSetupUntilStart || _openingSequenceStarted) return;
        _openingSequenceStarted = true;
        State.OpeningStage = OpeningStage.ResolvingOpeningEffects;
        _ = Track(CompleteInitialOpeningSequenceAsync());
    }

    private async Task CompleteInitialOpeningSequenceAsync()
    {
        for (int owner = 0; owner < State.Players.Length; owner++)
            await EffectRuntime.Resolve(State, owner, State.Players[owner].Leader, EffectTrigger.OnGameStart, Prompts);

        _leaderStartEffectsResolved = true;
        if (State.StartingPlayerChosen)
        {
            InitLifeAndHand(State.Players[0], 0);
            InitLifeAndHand(State.Players[1], 1);
            BeginMulliganPhase();
        }
        else
        {
            State.OpeningStage = OpeningStage.RollingDice;
            RollStartingDice();
            State.OpeningStage = OpeningStage.WaitingFirstPlayerChoice;
            State.StartingPlayerChoiceDeadlineUtc = DateTime.UtcNow.AddSeconds(StartingPlayerChoiceTimeoutSeconds);
        }

        OnOpeningSequenceReady?.Invoke();
        Broadcast("OpeningEffectsComplete");
    }

    // ── 引擎入口 ──────────────────────────────────────────────────────────

    public bool HandleAction(
        int playerIndex,
        string action,
        JsonElement data,
        string? requestId = null,
        GameActionSource source = GameActionSource.Player)
        => HandleActionWithReceipt(playerIndex, action, data, requestId, source).Accepted;

    /// <summary>线上 coordinator 专用入口；保留 accepted 日志的权威 seq/hash 绑定。</summary>
    internal GameActionExecutionReceipt HandleActionWithReceipt(
        int playerIndex,
        string action,
        JsonElement data,
        string? requestId = null,
        GameActionSource source = GameActionSource.Player)
    {
        if (State.IsGameOver) return new GameActionExecutionReceipt(false, null);

        var correlationId = GameActionSourceWire.CorrelationId(requestId, source);
        try
        {
            data = CanonicalJson.NormalizeObject(data);
        }
        catch (InvalidDataException ex)
        {
            RecordMatchLog("player_action_rejected", playerIndex, new
            {
                requestId = correlationId,
                action,
                source = GameActionSourceWire.Value(source),
                reason = ex.Message,
            });
            OnSendToPlayer?.Invoke(playerIndex, new
            {
                proto = "MsgActionRejected",
                reason = ex.Message,
                requestId = correlationId,
            });
            return new GameActionExecutionReceipt(false, null);
        }

        var dispatchStartedAt = LatencyDiagnostics.Start();
        BeginSnapshotBatch();
        _latencyActionStartedAt = dispatchStartedAt;
        _latencyAction = action;
        _latencyRequestId = correlationId;

        // 动作可能由新线程（重连/线程池）进入，重新挂上本局确定性 ID 工厂；
        // 由本动作启动的 async 续延会捕获该上下文并沿用同一计数器。
        DeterministicId.Current = _idFactory;

        _activeAction = action;
        _activeActor = playerIndex;
        _activeRequestId = correlationId;
        _activeActionSource = source;
        _activeActionRejected = false;

        // 撤回资格必须在后续动作的任何即时快照之前失效，否则 Attack/PlayCard 等屏障快照会
        // 短暂把已经失效的按钮重新下发。先暂存并清空；若动作最终被拒绝，再原样恢复。
        var undoStackBeforeOtherAction = action is "AttachDon" or "UndoAttachDon"
            ? null
            : State.AttachDonUndoStack.ToArray();
        if (undoStackBeforeOtherAction is { Length: > 0 })
            State.AttachDonUndoStack.Clear();

        // 平局申请期间冻结牌局，只允许对方回应或任一方直接投降。
        if (State.PendingDrawRequester is not null
            && action is not "RespondDraw" and not "Surrender")
        {
            SendError(playerIndex, "请先等待或处理当前平局申请");
        }
        // 卡牌效果等待玩家选择时，必须先完成当前效果链，不能让任何一方抢先推进牌局。
        // 投降、平局申请与回应不受卡牌 Prompt 限制。
        else if (State.PendingPrompt is not null
            && action is not "PromptResponse" and not "UndoAttachDon"
                and not "Surrender" and not "RequestDraw" and not "RespondDraw")
        {
            SendError(playerIndex, "当前有效果等待玩家处理，暂时无法执行其他操作");
        }
        else if (!State.StartingPlayerChosen
            && action is not "ChooseFirstPlayer" and not "PromptResponse"
                and not "Surrender" and not "RequestDraw" and not "RespondDraw")
        {
            SendError(playerIndex, "请先完成先后手选择");
        }
        else switch (action)
        {
            case "ChooseFirstPlayer": HandleChooseFirstPlayer(playerIndex, data); break;
            case "Mulligan":       HandleMulligan(playerIndex, data); break;
            case "PlayCard":       HandlePlayCard(playerIndex, data); break;
            case "AttachDon":      HandleAttachDon(playerIndex, data); break;
            case "UndoAttachDon":  HandleUndoAttachDon(playerIndex, data); break;
            case "Attack":         HandleAttack(playerIndex, data); break;
            case "DeclareBlocker": HandleDeclareBlocker(playerIndex, data); break;
            case "PassBlock":      HandlePassBlock(playerIndex); break;
            case "PlayCounter":    HandlePlayCounter(playerIndex, data); break;
            case "PassCounter":    HandlePassCounter(playerIndex); break;
            case "EndTurn":        HandleEndTurn(playerIndex); break;
            case "Surrender":      HandleSurrender(playerIndex); break;
            case "RequestDraw":    HandleRequestDraw(playerIndex, data); break;
            case "RespondDraw":    HandleRespondDraw(playerIndex, data); break;
            case "PromptResponse": HandlePromptResponse(playerIndex, data); break;
            case "UseEffect":      HandleUseEffect(playerIndex, data); break;
            case "DebugAddCard":   HandleDebugAddCard(playerIndex, data); break;
            case "DebugAddLife":   HandleDebugAddLife(playerIndex, data); break;
            case "DebugAddDon":    HandleDebugAddDon(playerIndex, data); break;
            case "DebugRefreshDon": HandleDebugRefreshDon(playerIndex); break;
            case "DebugSummon":    _ = HandleDebugSummonAsync(playerIndex, data); break;
            case "DebugKoAll":     _ = HandleDebugKoAllAsync(playerIndex, data); break;
            case "DebugRestAll":   HandleDebugRestAll(playerIndex, data); break;
            case "DebugLeaderAttack": _ = HandleDebugLeaderAttackAsync(playerIndex); break;
            case "DebugRunOP17Coverage": _ = Track(HandleDebugRunOP17CoverageAsync(playerIndex)); break;
            default:
                SendError(playerIndex, $"未知动作: {action}");
                break;
        }

        var accepted = !_activeActionRejected;
        if (!accepted && undoStackBeforeOtherAction is { Length: > 0 })
        {
            State.AttachDonUndoStack.Clear();
            State.AttachDonUndoStack.AddRange(undoStackBeforeOtherAction);
        }
        AcceptedActionLogReceipt? acceptedLog = null;
        if (accepted)
        {
            acceptedLog = RecordAcceptedAction(playerIndex, action, data, correlationId, source);
            OnPersistAction?.Invoke(playerIndex, action, data, correlationId); // 仅持久化被接受的动作
        }

        _activeAction = null;
        _activeActor = null;
        _activeRequestId = null;
        if (Volatile.Read(ref _trackedOperations) == 0)
            EndSnapshotBatch();
        LatencyDiagnostics.Observe("动作同步分派", dispatchStartedAt, $"房间={State.RoomId}，动作={action}");
        return new GameActionExecutionReceipt(accepted, acceptedLog);
    }

    // ── 开局骰点与先后手选择 ─────────────────────────────────────────────

    private void RollStartingDice()
    {
        while (true)
        {
            var player0 = State.Rng.Next(1, 7);
            var player1 = State.Rng.Next(1, 7);
            State.StartingDiceRounds.Add(new StartingDiceRound(player0, player1));
            var randomSeq = ++State.RandomSeq;
            RecordMatchLog("random_event", -1, new
            {
                randomSeq,
                type = "starting_dice",
                player0,
                player1,
                tie = player0 == player1,
                rngSeed = State.RngSeed,
            });

            if (player0 == player1) continue;
            State.StartingPlayerChooser = player0 > player1 ? 0 : 1;
            break;
        }
    }

    private void HandleChooseFirstPlayer(int playerIndex, JsonElement data)
    {
        if (State.StartingPlayerChosen)
        {
            SendError(playerIndex, "先后手已经确定");
            return;
        }
        if (State.StartingPlayerChooser != playerIndex)
        {
            SendError(playerIndex, "本次骰点胜者才可选择先后手");
            return;
        }
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("goFirst", out var goFirstElement)
            || goFirstElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            SendError(playerIndex, "缺少有效的先后手选择");
            return;
        }

        var goFirst = goFirstElement.GetBoolean();
        State.OpeningStage = OpeningStage.Mulligan;
        State.StartingPlayerChoiceDeadlineUtc = null;
        State.FirstPlayer = goFirst ? playerIndex : 1 - playerIndex;
        State.CurrentTurnPlayer = State.FirstPlayer;
        if (_deferOpeningSetupUntilFirstPlayerChosen)
        {
            _ = Track(CompleteDeferredOpeningSetupAsync(playerIndex, goFirst));
            return;
        }
        BeginMulliganPhase();
        Broadcast("FirstPlayerChosen", new
        {
            player = playerIndex,
            goFirst,
            firstPlayer = State.FirstPlayer,
        });
    }

    /// <summary>
    /// 骰点对局的正式开局准备：先按“先后手选择者 → 另一方”的顺序处理双方 OnGameStart，
    /// 再设置生命与起手牌并进入调度。OP13-079 因而会在任何卡牌进入起手或生命前检索舞台。
    /// </summary>
    private async Task CompleteDeferredOpeningSetupAsync(int chooserIndex, bool goFirst)
    {
        if (!_leaderStartEffectsResolved)
        {
            foreach (var owner in new[] { chooserIndex, 1 - chooserIndex })
                await EffectRuntime.Resolve(State, owner, State.Players[owner].Leader, EffectTrigger.OnGameStart, Prompts);
            _leaderStartEffectsResolved = true;
        }
        InitLifeAndHand(State.Players[0], 0);
        InitLifeAndHand(State.Players[1], 1);
        BeginMulliganPhase();
        Broadcast("FirstPlayerChosen", new
        {
            player = chooserIndex,
            goFirst,
            firstPlayer = State.FirstPlayer,
        });
    }

    /// <summary>
    /// 等待最近一次动作触发的最外层异步效果链进入"稳定态"：要么彻底解析完，要么挂起在
    /// 等待玩家响应的 PendingPrompt 上。供重放/恢复在喂下一个动作前调用，确保不会在效果
    /// 还没跑到稳定点时就塞入后续动作。timeoutMs 仅为防卡死的安全网，不参与对局逻辑。
    /// </summary>
    public Task WaitSettledAsync(int timeoutMs = 15000, string? resolvingPromptId = null)
        => WaitSettledCoreAsync(
            timeoutMs,
            resolvingPromptId,
            CancellationToken.None,
            propagateTrackedFailure: false);

    /// <summary>
    /// 训练工件重放专用的稳定等待：除超时外还响应外部取消，并传播已经结束的异步效果链异常。
    /// 线上恢复继续使用 <see cref="WaitSettledAsync"/> 的兼容语义，避免改变既有房间执行路径。
    /// </summary>
    internal Task WaitSettledForReplayAsync(
        int timeoutMs,
        string? resolvingPromptId,
        CancellationToken cancellationToken)
        => WaitSettledCoreAsync(
            timeoutMs,
            resolvingPromptId,
            cancellationToken,
            propagateTrackedFailure: true);

    private async Task WaitSettledCoreAsync(
        int timeoutMs,
        string? resolvingPromptId,
        CancellationToken cancellationToken,
        bool propagateTrackedFailure)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        // PromptResponse 入队后，先等待它所响应的旧 Prompt 被续延消费；若效果链创建了下一个
        // 新 Prompt，则新 Prompt 本身就是新的稳定点，不继续等待玩家输入。
        while (!_settle.IsCompleted
               && resolvingPromptId is not null
               && State.PendingPrompt?.PromptId == resolvingPromptId)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("WaitSettledAsync 超时：PromptResponse 未被效果链消费");
            await Task.WhenAny(_settle, Task.Delay(5, cancellationToken));
        }

        while (State.PendingPrompt is null && !_settle.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("WaitSettledAsync 超时：效果链未在限定时间内进入稳定态");
            await Task.WhenAny(_settle, Task.Delay(5, cancellationToken));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (propagateTrackedFailure && _settle.IsCompleted)
            await _settle.WaitAsync(cancellationToken);
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

    // ── GM 调试：按编号将卡牌置于指定一方生命区顶端 ─────────────────────────
    // 用于确定性验证生命【触发】，避免依赖洗牌后随机进入生命区。
    private void HandleDebugAddLife(int playerIndex, JsonElement data)
    {
        if (!data.TryGetProperty("cardNumber", out var cn) || cn.ValueKind != JsonValueKind.String)
        { SendError(playerIndex, "缺少 cardNumber"); return; }
        var number = cn.GetString()!.Trim();

        var info = CardDatabase.Get(number);
        if (info == null) { SendError(playerIndex, $"未知卡牌编号: {number}"); return; }

        var target = data.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : "self";
        var targetIndex = target == "opponent" ? 1 - playerIndex : playerIndex;
        var card = new CardInstance { Info = info, IsLifeFaceUp = false };
        State.Players[targetIndex].LifeArea.Insert(0, card);
        Broadcast("DebugAddLife", new { player = playerIndex, target = targetIndex, cardNumber = number });
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
                card.IsTapped = State.ShouldCharacterEnterRested(targetIndex, card);
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

            // 仅 KO 角色，不动 Leader/舞台；按同一次批量 KO 结算（触发 PreKO/置换/OnKO）。
            // GM 模拟"因对方效果被KO"：设置来源标记，使 OP16-024 等【因对方的效果被KO时】效果发动。
            // 仍用 KOCardAsync（非 KOByEffectAsync）：GM 不在效果上下文内，前者用 TriggerEvent 立即派发
            // OnAnyCharKOd，后者用 NotifyWatcher 会因无 ambient 被丢弃，反而破坏其它卡的 KO 联动。
            State.KOReason = "effect";
            State.KOActingSide = 1 - targetIndex;   // KO 方=被KO一方的对手
            try
            {
                await BattleEngine.KOCardsSimultaneouslyAsync(State, targetIndex, victims, Prompts);
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

    // ── GM 调试：按当前领航颜色运行 OP17 全卡独立场景巡检 ──────────────────
    private async Task HandleDebugRunOP17CoverageAsync(int playerIndex)
    {
        if (_op17CoverageRunning)
        {
            SendError(playerIndex, "本房间已有 OP17 巡检正在运行");
            return;
        }

        string color = State.Players[playerIndex].Leader.Info.ColorList.FirstOrDefault() ?? "";
        if (!OP17CoverageRunner.Colors().Contains(color, StringComparer.Ordinal))
        {
            SendError(playerIndex, $"当前领航颜色“{color}”不属于 OP17 巡检范围");
            return;
        }

        _op17CoverageRunning = true;
        Broadcast("DebugOP17CoverageStarted", new { player = playerIndex, color });
        try
        {
            var report = await OP17CoverageRunner.RunColorAsync(color);
            Broadcast("DebugOP17CoverageResult", new
            {
                player = playerIndex,
                color = report.Color,
                total = report.Total,
                passed = report.Passed,
                failed = report.Failed,
                results = report.Results.Select(result => new
                {
                    number = result.Number,
                    name = result.Name,
                    color = result.Color,
                    passed = result.Passed,
                    triggers = result.Triggers,
                    message = result.Message,
                }),
            });
        }
        catch (Exception ex)
        {
            Broadcast("DebugOP17CoverageResult", new
            {
                player = playerIndex,
                color,
                total = 0,
                passed = 0,
                failed = 1,
                results = Array.Empty<OP17CoverageCardResult>(),
                error = ex.Message,
            });
        }
        finally
        {
            _op17CoverageRunning = false;
        }
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
            // 新客户端会把腾位目标与出牌合并提交，省掉一次服务端 Prompt 往返；
            // 未携带该字段的旧客户端仍走下面的兼容 Prompt 流程。
            if (data.TryGetProperty("overflowTrashCardId", out var victimIdElem))
            {
                if (victimIdElem.ValueKind != JsonValueKind.String
                    || !Guid.TryParse(victimIdElem.GetString(), out var victimId))
                {
                    SendError(playerIndex, "腾位角色 ID 无效");
                    return;
                }

                var victim = p.Characters.FirstOrDefault(c => c.Id == victimId);
                if (victim is null)
                {
                    SendError(playerIndex, "所选腾位角色已不在角色区");
                    return;
                }

                p.Characters.Remove(victim);
                p.Trash.Add(victim);
            }
            else
            {
                _ = Track(PlayCharacterWithOverflowAsync(playerIndex, handCard));
                return;
            }
        }

        var cardNumber = handCard.Info.Number;
        var result = CardPlayer.Play(State, playerIndex, handIndex);
        // 等效果链进入稳定点后只广播一次最终出牌状态；效果中若产生 Prompt，Prompt 自身会先广播当前牌桌。
        _ = Track(ResolveEffectAndBroadcastPlayAsync(playerIndex, result, cardNumber));
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
            await ResolveEffectAndBroadcastPlayAsync(playerIndex, result, cardNumber);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Play] 满员登场异常: {ex.Message}"); }
    }

    private async Task ResolveEffectAndBroadcastPlayAsync(int playerIndex, PlayResult result, string cardNumber)
    {
        var playPayload = new
        {
            player = playerIndex,
            cardNumber,
            kind = result.Kind.ToString(),
            cardId = result.Card.Id.ToString(),
        };
        // 先排入日志，保证遇到登场效果 Prompt 时仍按“出牌 → 选择 → 公开”显示；
        // 实际 PlayCard 快照仍在结算结束后广播，保持既有动画与状态时机。
        QueueActionLog("PlayCard", playPayload);
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

                // 旁观者：当对方发动事件时（监听卡为非出牌方，脚本内自行判定）
                if (!State.IsGameOver)
                    await EffectRuntime.TriggerEvent(State, EffectTrigger.OnOppEventPlayed, Prompts,
                        new Dictionary<string, object?> { ["owner"] = playerIndex });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Effects] {result.Card.Info.Number} 解析异常: {ex.Message}");
        }
        finally
        {
            // 保留 PlayCard 动画/日志语义，同时避免此前 PlayCard + EffectResolved 两份连续完整快照。
            Broadcast("PlayCard", new
            {
                player = playerIndex,
                cardNumber,
                kind = result.Kind.ToString(),
                cardId = result.Card.Id.ToString(),
                suppressLog = true,
            });
        }
    }

    // ── 赋予咚 ────────────────────────────────────────────────────────────

    private void HandleAttachDon(int playerIndex, JsonElement data)
    {
        if (!data.TryGetProperty("targetId", out var ti) || ti.ValueKind != JsonValueKind.String)
        { SendError(playerIndex, "缺少 targetId"); return; }
        var targetIdStr = ti.GetString()!;
        int count = 1;
        if (data.TryGetProperty("count", out var countElement)
            && (countElement.ValueKind != JsonValueKind.Number || !countElement.TryGetInt32(out count)))
        {
            SendError(playerIndex, "赋予咚数量必须是整数");
            return;
        }

        var v = ActionValidator.CanAttachDon(State, playerIndex, targetIdStr, count);
        if (!v.Ok) { SendError(playerIndex, v.Reason!); return; }

        var p = State.Players[playerIndex];
        Guid targetId = targetIdStr == "leader" ? p.Leader.Id : Guid.Parse(targetIdStr);
        // 先取得完整批次再变更，确保伪造的超额/负数请求不会出现部分赋予。
        var activeDons = p.CostArea.Where(don => don.State == DonState.Active).Take(count).ToArray();
        if (activeDons.Length != count)
        {
            SendError(playerIndex, $"活跃咚不足，需要 {count} 张");
            return;
        }
        foreach (var don in activeDons)
        {
            don.State = DonState.Attached;
            don.AttachedToCardId = targetId;
        }
        var operationSequence = ++State.AttachDonOperationSequence;
        State.AttachDonUndoStack.Add(new AttachDonUndoEntry(
            operationSequence,
            playerIndex,
            targetIdStr,
            targetId,
            activeDons.Select(don => don.Id).ToArray()));
        // 赋予咚!! 时触发（OP02-002 戈普领袖：我方回合赋予咚→对方角色减费），
        // 效果链稳定后再以 AttachDon 语义发送最终快照，避免 AttachDon + EffectResolved 连发两包。
        _resolvingAttachDonOperationSequence = operationSequence;
        _ = Track(ResolveDonAttachedAsync(playerIndex, targetId, targetIdStr, count, operationSequence));
    }

    /// <summary>赋予咚!! 后派发 OnDonAttached（监听卡如 OP02-002 据此发动），随后刷新状态</summary>
    private async Task ResolveDonAttachedAsync(
        int playerIndex,
        Guid targetId,
        string targetIdStr,
        int count,
        long operationSequence)
    {
        var canceledByUndo = false;
        try
        {
            await EffectRuntime.TriggerEvent(State, EffectTrigger.OnDonAttached, Prompts,
                new Dictionary<string, object?> { ["targetId"] = targetId.ToString(), ["owner"] = playerIndex });
            CheckGameOver();
        }
        catch (AttachDonUndoCanceledException)
        {
            // 撤回动作已负责恢复咚与下发权威快照；取消属于正常路径，不记录为卡效异常。
            canceledByUndo = true;
        }
        catch (Exception ex) { Console.Error.WriteLine($"[DonAttached] 派发异常: {ex.Message}"); }
        finally
        {
            if (_resolvingAttachDonOperationSequence == operationSequence)
                _resolvingAttachDonOperationSequence = null;
            if (!canceledByUndo && !State.IsGameOver)
                Broadcast("AttachDon", new
                {
                    player = playerIndex,
                    targetId = targetIdStr,
                    count,
                    operationId = operationSequence.ToString(),
                });
        }
    }

    private void HandleUndoAttachDon(int playerIndex, JsonElement data)
    {
        if (!data.TryGetProperty("operationId", out var operationElement)
            || operationElement.ValueKind != JsonValueKind.String
            || !long.TryParse(operationElement.GetString(), out var operationSequence)
            || operationSequence <= 0)
        {
            SendError(playerIndex, "撤回贴咚操作序号非法");
            return;
        }

        var validation = ActionValidator.CanUndoAttachDon(State, playerIndex, operationSequence);
        if (!validation.Ok) { SendError(playerIndex, validation.Reason!); return; }

        var entry = State.AttachDonUndoStack[^1];
        var player = State.Players[playerIndex];
        var dons = entry.DonIds
            .Select(id => player.CostArea.FirstOrDefault(don => don.Id == id))
            .ToArray();
        if (dons.Any(don => don is null)
            || dons.Any(don => don!.State != DonState.Attached || don.AttachedToCardId != entry.TargetCardId))
        {
            // 先完整核对再变更，绝不在内部状态已偏离时做部分撤回。
            SendError(playerIndex, "贴咚状态已经变化，无法撤回");
            return;
        }

        if (_resolvingAttachDonOperationSequence is { } resolvingSequence)
        {
            if (resolvingSequence != operationSequence
                || State.PendingPrompt is null
                || !Prompts.CancelCurrentForAttachDonUndo())
            {
                SendError(playerIndex, "贴咚效果仍在结算，请稍后再试");
                return;
            }
        }
        else if (State.PendingPrompt is not null)
        {
            // 普通效果 Prompt 与贴咚撤回无关，不允许借撤回动作取消。
            SendError(playerIndex, "当前有效果等待处理，无法撤回贴咚");
            return;
        }

        foreach (var don in dons)
        {
            don!.State = DonState.Active;
            don.AttachedToCardId = null;
        }
        State.AttachDonUndoStack.RemoveAt(State.AttachDonUndoStack.Count - 1);
        Broadcast("UndoAttachDon", new
        {
            player = playerIndex,
            targetId = entry.TargetId,
            count = dons.Length,
            operationId = operationSequence.ToString(),
        });
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

        // 攻击前置弃牌税（OP08-043：仅对方“角色”攻击前须弃 N 张手牌）。
        // 领袖与角色共用 Attack 入口，必须先按权威场上实例判定攻击者类型，
        // 否则领袖攻击也会被错误征税；角色原有征税与异步选择链保持不变。
        bool isCharacterAttack = State.Players[playerIndex].Characters.Any(card => card.Id == attackerId);
        int tax = isCharacterAttack ? State.AttackTaxDiscard[playerIndex] : 0;
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
            if (!BattleEngine.AreBattleParticipantsOnField(State))
            {
                await CompleteBattleAsync();
                return;
            }

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

    private Task AdvanceBattleAfterBlockAsync()
    {
        if (State.CurrentBattle is null) return Task.CompletedTask;

        // 无论防守方手牌是否存在可用反击，都必须进入反击等待。
        // 否则自动跳过会向对手泄露防守方手中没有反击值或反击事件。
        Broadcast("AwaitCounter");
        return Task.CompletedTask;
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
            if (!BattleEngine.AreBattleParticipantsOnField(State))
            {
                await CompleteBattleAsync();
                return;
            }
            // 旁观者：当对方发动【阻挡者】时（监听卡为攻击方，脚本内判 blockerOwner != self）
            int blockerOwner = State.CurrentBattle.DefenderPlayerIndex;
            await EffectRuntime.TriggerEvent(State, EffectTrigger.OnOppBlocker, Prompts,
                new Dictionary<string, object?> { ["blockerOwner"] = blockerOwner });
            if (State.IsGameOver || State.CurrentBattle is null) { CheckGameOver(); return; }
            if (!BattleEngine.AreBattleParticipantsOnField(State))
            {
                await CompleteBattleAsync();
                return;
            }
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
            // 从手牌选一张有反击值的卡牌（通常为角色；OP18-021 可使舞台获得反击），
            // 丢入废弃区，并给当前被攻击目标加力量。
            if (!data.TryGetProperty("handIndex", out var hi) || hi.ValueKind != JsonValueKind.Number)
            { SendError(playerIndex, "缺少 handIndex"); return; }
            int handIndex = hi.GetInt32();
            if (handIndex < 0 || handIndex >= def.Hand.Count) { SendError(playerIndex, "手牌索引非法"); return; }
            var counterCard = def.Hand[handIndex];
            int counterValue = HandStaticCounter.Value(State, playerIndex, counterCard);
            if (counterValue <= 0) { SendError(playerIndex, "该卡无反击值"); return; }
            def.Hand.RemoveAt(handIndex);
            def.Trash.Add(counterCard);
            BattleEngine.ApplyCounter(State, playerIndex, counterValue);
            Broadcast("CounterIcon", new { handIndex, value = counterValue });
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
            if (!State.IsGameOver)
                await EffectRuntime.TriggerEvent(State, EffectTrigger.OnOppEventPlayed, Prompts,
                    new Dictionary<string, object?> { ["owner"] = playerIndex });
            Broadcast("EffectResolved", new { cardNumber });
            if (State.IsGameOver || State.CurrentBattle is null) { CheckGameOver(); return; }
            if (!BattleEngine.AreBattleParticipantsOnField(State))
            {
                await CompleteBattleAsync();
                return;
            }
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
            await CompleteBattleAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Battle] 异步伤害处理异常: {ex.Message}");
        }
    }

    private async Task CompleteBattleAsync()
    {
        // 【战斗结束时】触发：在 EndBattle 清除 CurrentBattle 前派发（监听卡可读 CurrentBattle 判断是否参战）
        if (!State.IsGameOver && State.CurrentBattle is { } battle)
        {
            await EffectRuntime.TriggerEvent(State, EffectTrigger.OnBattleEnd, Prompts,
                new Dictionary<string, object?>
                {
                    ["attackerId"] = battle.AttackerCardId.ToString(),
                    ["attackerPlayerIdx"] = battle.AttackerPlayerIndex,
                    ["defenderPlayerIdx"] = battle.DefenderPlayerIndex,
                    ["targetIsLeader"] = battle.TargetIsLeader,
                    ["targetCardId"] = (battle.ReplacedByBlockerCardId ?? battle.TargetCardId)?.ToString(),
                });
        }
        BattleEngine.EndBattle(State);
        Broadcast("BattleEnd");
        CheckGameOver();
    }

    private void CheckGameOver()
    {
        State.EvaluateDeckOut();
        if (State.IsGameOver)
            Broadcast("DuelOver", new { winner = State.WinnerIndex, reason = State.GameOverReason });
    }

    public void BroadcastInitialState()
    {
        BeginOpeningSequence();
        var startedAt = LatencyDiagnostics.Start();
        BeforeSnapshot?.Invoke();
        State.Tick++;
        CaptureReplayHands();
        var snapshots = StateSnapshotBuilder.BuildAll(
            State,
            "GameStart",
            includePlayer1Spectator: HasSpectatorsForPerspective?.Invoke(1) == true,
            includePlayer0SpectatorHand: HasSpectatorsWithHandForPerspective?.Invoke(0) == true,
            includePlayer1SpectatorHand: HasSpectatorsWithHandForPerspective?.Invoke(1) == true,
            replayHandTimeline: _replayHandTimeline);
        OnSendToPlayer?.Invoke(0, snapshots.Player0);
        OnSendToPlayer?.Invoke(1, snapshots.Player1);
        var publicSnapshot = snapshots.Spectator;
        if (HasSpectators?.Invoke() != false)
        {
            OnSendToSpectators?.Invoke(0, publicSnapshot, snapshots.SpectatorPlayer0Hand);
            if (snapshots.SpectatorPlayer1 is not null)
                OnSendToSpectators?.Invoke(1, snapshots.SpectatorPlayer1, snapshots.SpectatorPlayer1Hand);
        }
        var sharedPublicSnapshot = new SharedJsonValue(publicSnapshot);
        RecordMatchLog("public_snapshot", -1, sharedPublicSnapshot);
        if (EnablePrivateSnapshotLog)
            RecordMatchLog("private_snapshot", -1, PrivateStateSnapshotBuilder.Build(State));
        LatencyDiagnostics.Observe("初始快照构建与入队", startedAt, $"房间={State.RoomId}，Tick={State.Tick}");
    }

    private void BeginSnapshotBatch()
    {
        lock (_snapshotBatchGate)
        {
            if (_snapshotBatchActive) return;
            _snapshotBatchActive = true;
            _pendingBroadcast = null;
        }
    }

    private void EndSnapshotBatch()
    {
        PendingBroadcast? pending;
        lock (_snapshotBatchGate)
        {
            if (!_snapshotBatchActive || Volatile.Read(ref _trackedOperations) != 0) return;
            pending = _pendingBroadcast;
            _pendingBroadcast = null;
            _snapshotBatchActive = false;
        }

        if (pending is not null)
            BroadcastNow(pending.LastAction, pending.Payload);
        _latencyActionStartedAt = 0;
        _latencyAction = "";
        _latencyRequestId = null;
    }

    /// <summary>
    /// 动作结算期间，普通中间状态只保留最后一份；交互与关键动画屏障立即发送。
    /// 不在 HandleAction/Track 链内的广播保持原有即时行为。
    /// </summary>
    public void Broadcast(string lastAction, object? payload = null)
    {
        var sendNow = false;
        lock (_snapshotBatchGate)
        {
            if (!_snapshotBatchActive)
            {
                sendNow = true;
            }
            else if (ImmediateSnapshotActions.Contains(lastAction))
            {
                // 当前完整快照已包含此前所有状态，旧的普通中间快照无需再发。
                _pendingBroadcast = null;
                sendNow = true;
            }
            else
            {
                _pendingBroadcast = new PendingBroadcast(lastAction, payload);
            }
        }

        if (sendNow)
            BroadcastNow(lastAction, payload);
    }

    /// <summary>
    /// 将一条操作日志暂存到下一份状态快照。用于一次效果结算内可能出现的多次选择，
    /// 避免为了日志额外发送完整牌桌快照。
    /// </summary>
    public void QueueActionLog(string action, object? payload)
    {
        lock (_snapshotBatchGate)
            _pendingActionLogs.Add(new ActionLogEvent(action, payload));
    }

    /// <summary>
    /// 把卡牌效果发动表现暂存到下一份快照。一个结算批次内可记录多张来源卡，
    /// 防止只保留 lastAction 时连锁效果的中间来源被覆盖。
    /// </summary>
    public void QueueEffectActivation(int ownerIndex, CardInstance source, EffectTrigger trigger)
    {
        // 对局开始效果用于注册静态被动，不应在开局阶段播放发动特效。
        if (trigger == EffectTrigger.OnGameStart) return;

        lock (_snapshotBatchGate)
        {
            _pendingEffectActivations.Add(new EffectActivationEvent(
                ownerIndex,
                source.Id,
                source.Info.Number,
                trigger.ToString()));
        }
    }

    private void BroadcastNow(string lastAction, object? payload)
    {
        var startedAt = LatencyDiagnostics.Start();
        BeforeSnapshot?.Invoke();
        ActionLogEvent[] queuedLogEvents;
        EffectActivationEvent[] queuedEffectActivations;
        lock (_snapshotBatchGate)
        {
            queuedLogEvents = _pendingActionLogs.ToArray();
            _pendingActionLogs.Clear();
            queuedEffectActivations = _pendingEffectActivations.ToArray();
            _pendingEffectActivations.Clear();
        }
        State.Tick++;
        CaptureReplayHands();
        var snapshots = StateSnapshotBuilder.BuildAll(
            State,
            lastAction,
            payload,
            queuedLogEvents,
            requestId: _latencyRequestId,
            effectActivations: queuedEffectActivations,
            includePlayer1Spectator: HasSpectatorsForPerspective?.Invoke(1) == true,
            includePlayer0SpectatorHand: HasSpectatorsWithHandForPerspective?.Invoke(0) == true,
            includePlayer1SpectatorHand: HasSpectatorsWithHandForPerspective?.Invoke(1) == true,
            replayHandTimeline: _replayHandTimeline);
        OnSendToPlayer?.Invoke(0, snapshots.Player0);
        OnSendToPlayer?.Invoke(1, snapshots.Player1);
        var publicSnapshot = snapshots.Spectator;
        if (HasSpectators?.Invoke() != false)
        {
            OnSendToSpectators?.Invoke(0, publicSnapshot, snapshots.SpectatorPlayer0Hand);
            if (snapshots.SpectatorPlayer1 is not null)
                OnSendToSpectators?.Invoke(1, snapshots.SpectatorPlayer1, snapshots.SpectatorPlayer1Hand);
        }
        var sharedPublicSnapshot = new SharedJsonValue(publicSnapshot);
        RecordMatchLog("public_snapshot", -1, sharedPublicSnapshot);
        if (EnablePrivateSnapshotLog)
            RecordMatchLog("private_snapshot", -1, PrivateStateSnapshotBuilder.Build(State));
        LatencyDiagnostics.Observe("快照构建与入队", startedAt, $"房间={State.RoomId}，Tick={State.Tick}，事件={lastAction}");
        if (_latencyActionStartedAt != 0)
            LatencyDiagnostics.Observe("动作到快照", _latencyActionStartedAt, $"房间={State.RoomId}，动作={_latencyAction}，事件={lastAction}");
    }

    /// <summary>
    /// 记录当前双方手牌与生命区；仅在牌号或顺序变化时追加，避免终局回放数据随普通状态快照膨胀。
    /// 重启恢复重放同样会经过广播路径，因此能自然重建完整时间线。
    /// </summary>
    private void CaptureReplayHands()
    {
        var player0 = State.Players[0].Hand.Select(card => card.Info.Number).ToArray();
        var player1 = State.Players[1].Hand.Select(card => card.Info.Number).ToArray();
        var player0Life = State.Players[0].LifeArea.Select(card => card.Info.Number).ToArray();
        var player1Life = State.Players[1].LifeArea.Select(card => card.Info.Number).ToArray();
        var previous = _replayHandTimeline.LastOrDefault();
        if (previous is not null
            && previous.Player0CardNumbers.SequenceEqual(player0)
            && previous.Player1CardNumbers.SequenceEqual(player1)
            && previous.Player0LifeCardNumbers.SequenceEqual(player0Life)
            && previous.Player1LifeCardNumbers.SequenceEqual(player1Life))
            return;

        _replayHandTimeline.Add(new StateSnapshotBuilder.ReplayHandFrame(
            State.Tick, player0, player1, player0Life, player1Life));
    }

    /// <summary>短暂向双方公开 ownerIndex 检索到的牌（搭一次广播即清空），客户端弹出展示浮层</summary>
    public void BroadcastReveal(int ownerIndex, IReadOnlyList<string> cardNumbers)
    {
        if (cardNumbers.Count == 0) return;
        State.PendingReveal = new RevealInfo { OwnerIndex = ownerIndex, CardNumbers = cardNumbers.ToList() };
        Broadcast("RevealCards", new { player = ownerIndex, cardNumbers = cardNumbers.ToArray() });
        State.PendingReveal = null;
    }

    public void SendError(int playerIndex, string reason)
    {
        _activeActionRejected = true;
        RecordMatchLog("player_action_rejected", _activeActor ?? playerIndex, new
        {
            requestId = _activeRequestId ?? GameActionSourceWire.CorrelationId(null, _activeActionSource),
            action = _activeAction ?? "",
            source = GameActionSourceWire.Value(_activeActionSource),
            reason,
        });
        OnSendToPlayer?.Invoke(playerIndex, new { proto = "MsgActionRejected", reason, requestId = _latencyRequestId });
    }

    /// <summary>accepted 的唯一写入形态；data 已规范化并且来源不会由调用者伪装成真人。</summary>
    internal AcceptedActionLogReceipt? RecordAcceptedAction(
        int playerIndex,
        string action,
        JsonElement data,
        string requestId,
        GameActionSource source)
    {
        var normalized = CanonicalJson.NormalizeObject(data);
        var logReceipt = RecordMatchLog("player_action_accepted", playerIndex, new
        {
            requestId,
            action,
            data = normalized,
            source = GameActionSourceWire.Value(source),
        });
        if (logReceipt is null) return null;

        var replaySource = source == GameActionSource.Player
            ? ReplayActionSource.Player
            : ReplayActionSource.System;
        var canonical = AcceptedActionCanonicalizer.Create(
            logReceipt.Value.Seq,
            logReceipt.Value.Seq,
            logReceipt.Value.Seq,
            playerIndex,
            action,
            normalized,
            replaySource);
        return new AcceptedActionLogReceipt(
            canonical.OrderSeq,
            canonical.StableHash,
            logReceipt.Value.Queued);
    }

    public MatchLogAppendReceipt? RecordMatchLog(string kind, int? actor, object? payload)
    {
        if (OnMatchLogWithReceipt is not null)
            return OnMatchLogWithReceipt.Invoke(kind, actor, payload);
        if (OnMatchLog is null)
        {
            _pendingMatchLogs.Add((kind, actor, payload));
            return null;
        }
        OnMatchLog.Invoke(kind, actor, payload);
        return null;
    }

    public void FlushPendingMatchLogs()
    {
        if ((OnMatchLog is null && OnMatchLogWithReceipt is null) || _pendingMatchLogs.Count == 0) return;
        foreach (var (kind, actor, payload) in _pendingMatchLogs)
            RecordMatchLog(kind, actor, payload);
        _pendingMatchLogs.Clear();
    }

    // ── Mulligan ─────────────────────────────────────────────────────────

    private void BeginMulliganPhase()
    {
        State.OpeningStage = OpeningStage.Mulligan;
        State.MulliganDeadlineUtc = DateTime.UtcNow.AddSeconds(MulliganTimeoutSeconds);
    }

    /// <summary>调度截止时，令所有尚未决定的玩家自动保留手牌。由房间串行队列调用。</summary>
    public IReadOnlyList<int> AutoKeepMulligans(DateTime utcNow)
    {
        if (State.MulliganDeadlineUtc is not { } deadline
            || utcNow < deadline
            || State.MulliganBothDone)
            return Array.Empty<int>();

        var autoKept = new List<int>();
        for (int playerIndex = 0; playerIndex < State.Players.Length; playerIndex++)
        {
            if (State.Players[playerIndex].MulliganDone) continue;
            CompleteMulligan(playerIndex, redraw: false);
            autoKept.Add(playerIndex);
        }
        return autoKept;
    }

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

        CompleteMulligan(playerIndex, redraw);
    }

    private void CompleteMulligan(int playerIndex, bool redraw)
    {
        var p = State.Players[playerIndex];
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
            State.MulliganDeadlineUtc = null;
            CaptureStartingHands();
            TurnEngine.StartFirstTurn(State);
            State.OpeningStage = OpeningStage.Playing;
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

    private void CaptureStartingHands()
    {
        for (var playerIndex = 0; playerIndex < State.Players.Length; playerIndex++)
        {
            var cards = State.StartingHandCardNumbers[playerIndex];
            cards.Clear();
            cards.AddRange(State.Players[playerIndex].Hand.Select(card => card.Info.Number));
        }
    }

    /// <summary>
    /// 开局注册双方领袖的永续被动（OnGameStart）。领袖被动应为纯状态写入、不触发 prompt，
    /// 故 Resolve 同步跑完即完成注册；OnGameStart 对无此被动的领袖（含纯 DSL 卡）为 no-op。
    /// </summary>
    private void RegisterLeaderPassives()
    {
        if (_leaderStartEffectsResolved) return;
        _leaderStartEffectsResolved = true;
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
        var effectPayload = new { source = source.Id.ToString(), card = source.Info.Number };
        QueueActionLog("UseEffect", effectPayload);
        try
        {
            await EffectRuntime.Resolve(State, playerIndex, source, EffectTrigger.ActivatedMain, Prompts);
            Broadcast("UseEffect", new { source = source.Id.ToString(), card = source.Info.Number, suppressLog = true });
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
        var pending = State.PendingPrompt;
        if (pending is null) { SendError(playerIndex, "没有待响应的 prompt"); return; }
        if (pending.PlayerIndex != playerIndex) { SendError(playerIndex, "不是你的 prompt"); return; }
        var promptId = data.TryGetProperty("promptId", out var pi) ? pi.GetString() ?? "" : "";
        if (promptId != pending.PromptId) { SendError(playerIndex, "promptId 不匹配"); return; }
        var chosen = new List<string>();
        if (data.TryGetProperty("chosen", out var ch) && ch.ValueKind == JsonValueKind.Array)
            foreach (var item in ch.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                    chosen.Add(item.GetString()!);

        // 客户端响应只是输入，不能借由空选、超量、重复或伪造 ID 跳过必选成本/效果。
        // 超时仍由 PromptSystem 自己处理；这里仅拒绝玩家主动提交的不合法答案，并保留原 prompt。
        if (chosen.Count < pending.MinChoose || chosen.Count > pending.MaxChoose)
        { SendError(playerIndex, "选择数量不符合要求"); return; }
        if (chosen.Distinct(StringComparer.Ordinal).Count() != chosen.Count)
        { SendError(playerIndex, "不能重复选择同一项"); return; }
        if (chosen.Any(id => !pending.ValidChoices.Contains(id)))
        { SendError(playerIndex, "包含不可选择的项目"); return; }

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
            State.EvaluateDeckOut(endOfTurn: true);
            if (State.IsGameOver) { CheckGameOver(); return; }

            await TurnEngine.ResolvePromptedEndPhaseTasksAsync(State, Prompts);
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
        State.ClearPendingDrawRequest();
        State.WinnerIndex = 1 - playerIndex;
        State.GameOverReason = $"{State.Players[playerIndex].VisibleName} 投降";
        Broadcast("Surrender", new { surrendered = playerIndex });
    }

    // ── 协商平局 ────────────────────────────────────────────────────────

    private void HandleRequestDraw(int playerIndex, JsonElement data)
    {
        if (State.MatchKind == MatchKind.Bot)
        {
            SendError(playerIndex, "机器人对局无法请求平局");
            return;
        }
        if (State.PendingDrawRequester is not null)
        {
            SendError(playerIndex, "当前已有平局申请等待回应");
            return;
        }
        if (State.DrawRequestRejectionCounts[playerIndex] >= GameState.DrawRequestRejectionLimit)
        {
            SendError(playerIndex, "本局平局申请已连续被拒绝 3 次，无法再次申请");
            return;
        }
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("description", out var descriptionElement))
        {
            SendError(playerIndex, "请填写发生了什么 Bug");
            return;
        }
        if (descriptionElement.ValueKind != JsonValueKind.String)
        {
            SendError(playerIndex, "Bug 描述格式无效，请填写文字");
            return;
        }

        var description = descriptionElement.GetString()?.Trim() ?? "";
        if (description.Length == 0)
        {
            SendError(playerIndex, "请填写发生了什么 Bug");
            return;
        }
        if (description.Length > GameState.DrawRequestDescriptionMaxLength)
        {
            SendError(playerIndex, $"Bug 描述不能超过 {GameState.DrawRequestDescriptionMaxLength} 个字符");
            return;
        }

        State.SetPendingDrawRequest(playerIndex, description);
        Broadcast("DrawRequested", new { requester = playerIndex });
    }

    private void HandleRespondDraw(int playerIndex, JsonElement data)
    {
        if (State.PendingDrawRequester is not int requester)
        {
            SendError(playerIndex, "当前没有等待回应的平局申请");
            return;
        }
        if (requester == playerIndex)
        {
            SendError(playerIndex, "发起者不能回应自己的平局申请");
            return;
        }
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("accept", out var acceptElement)
            || acceptElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            SendError(playerIndex, "缺少有效的平局回应");
            return;
        }

        State.ClearPendingDrawRequest();
        if (!acceptElement.GetBoolean())
        {
            State.DrawRequestRejectionCounts[requester] = Math.Min(
                GameState.DrawRequestRejectionLimit,
                State.DrawRequestRejectionCounts[requester] + 1);
            Broadcast("DrawRequestRejected", new
            {
                requester,
                responder = playerIndex,
                rejectionCount = State.DrawRequestRejectionCounts[requester],
            });
            return;
        }

        State.WinnerIndex = null;
        State.IsDraw = true;
        State.GameOverReason = "双方同意因 Bug 平局";
        Broadcast("DrawAgreed", new { requester, responder = playerIndex });
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
