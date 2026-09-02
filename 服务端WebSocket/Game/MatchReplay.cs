using System.Text.Json;
using GrandUMI.Effects.Rules;

namespace GrandUMI.Game;

/// <summary>
/// 对局重放驱动：用原始 seed + 牌组 + 有序动作磁带，重新执行一遍引擎以重建对局状态。
///
/// 这是"服务器重启后恢复对局"的核心：重启时不去序列化无法序列化的内存状态（永续效果委托、
/// 挂起的 async 续延），而是重新构造引擎并按序喂回当时记录的动作，让状态自然长回来。
///
/// 重放期间引擎不挂任何回调（OnSendToPlayer/OnBroadcast/OnMatchLog 全为 null），
/// 故所有对外广播、日志、录像在重放期间天然为 no-op，不会污染线上或重复落盘。
/// </summary>
public static class MatchReplay
{
    /// <summary>一条可重放的动作（与 GameEngine.HandleAction 的入参一一对应）。</summary>
    public sealed record ActionEntry(
        int PlayerIndex,
        string Action,
        JsonElement Data,
        GameActionSource Source = GameActionSource.Player,
        string? HexDraftRoundId = null,
        DateTime? HexDraftDeadlineUtc = null);

    /// <summary>严格工件重放到达的稳定点；ActionIndex=-1 表示开局稳定点。</summary>
    internal sealed record SettledReplayPoint(
        int ActionIndex,
        ActionEntry? Action,
        GameEngine Engine);

    internal sealed class ReplayActionRejectedException(
        int actionIndex,
        ActionEntry action)
        : Exception($"重放动作被引擎拒绝：index={actionIndex}，actor={action.PlayerIndex}，action={action.Action}")
    {
        public int ActionIndex { get; } = actionIndex;
        public ActionEntry ActionEntry { get; } = action;
    }

    internal sealed class ReplayTapeAfterGameOverException(int actionIndex)
        : Exception($"对局已终局，但动作磁带仍包含 index={actionIndex} 及其后续动作")
    {
        public int ActionIndex { get; } = actionIndex;
    }

    /// <summary>构造一条无 payload 的动作（如 PassBlock/EndTurn）。</summary>
    public static ActionEntry Action(int playerIndex, string action)
        => new(playerIndex, action, EmptyObject());

    /// <summary>从 JSON 文本构造一条动作的 data。</summary>
    public static ActionEntry Action(int playerIndex, string action, string dataJson)
        => new(playerIndex, action, JsonDocument.Parse(dataJson).RootElement.Clone());

    private static JsonElement EmptyObject() => JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>
    /// 重建对局：构造静默引擎，按序喂入动作，每个动作后等待其异步效果链进入稳定态。
    /// 返回重建出的引擎（其 State 即恢复出的对局状态）。
    /// </summary>
    public static async Task<GameEngine> RebuildAsync(
        string roomId,
        int seed,
        int firstPlayer,
        (string account, string deckRaw) p0,
        (string account, string deckRaw) p1,
        IReadOnlyList<ActionEntry> actions,
        bool leaderKeywordWildcard = false,
        bool p0AlwaysPrompt = false,
        bool p1AlwaysPrompt = false,
        bool openingSetupAfterFirstPlayerChoice = false,
        CardRuleset? ruleset = null,
        MatchKind matchKind = MatchKind.UnknownHuman,
        bool hexMode = false,
        int hexRulesRevision = Hex.HexRules.CurrentRulesRevision,
        Hex.HexCatalogConfiguration? hexCatalogConfiguration = null)
    {
        var engine = new GameEngine(
            roomId,
            ("replay-p0", p0.account, p0.deckRaw),
            ("replay-p1", p1.account, p1.deckRaw),
            firstPlayer: firstPlayer,
            rngSeed: seed,
            leaderKeywordWildcard: leaderKeywordWildcard,
            deferOpeningSetupUntilFirstPlayerChosen: openingSetupAfterFirstPlayerChoice,
            deferInitialSetupUntilStart: openingSetupAfterFirstPlayerChoice,
            ruleset: ruleset,
            matchKind: matchKind,
            hexMode: hexMode);
        Hex.HexRules.SetRulesRevisionForReplay(engine.State, hexRulesRevision);
        if (hexCatalogConfiguration is not null)
            Hex.HexRules.SetCatalogForReplay(engine.State, hexCatalogConfiguration);

        // 必须在喂入动作之前恢复"防触发信息泄露"开关：它决定生命揭示是否暂停发 prompt，
        // 进而决定动作磁带里有没有对应的 PromptResponse —— 不还原会导致重放分歧。
        engine.State.Players[0].AlwaysPromptOnLifeReveal = p0AlwaysPrompt;
        engine.State.Players[1].AlwaysPromptOnLifeReveal = p1AlwaysPrompt;

        if (openingSetupAfterFirstPlayerChoice)
            engine.BeginOpeningSequence();

        // 构造后理论上无在途链，仍等一次以防万一
        await engine.WaitSettledAsync();

        foreach (var a in actions)
        {
            if (engine.State.IsGameOver) break;
            // 功能上线前 RequestDraw 没有 description。旧动作日志必须仍能重建，
            // 但实时请求继续由 GameEngine 严格拒绝空描述，避免兼容逻辑削弱新协议校验。
            var replayData = PrepareReplayData(a);
            engine.HandleAction(a.PlayerIndex, a.Action, replayData, source: a.Source);
            await engine.WaitSettledAsync();
            RestoreHexDraftDeadline(engine, a);
        }

        return engine;
    }

    /// <summary>
    /// artifact worker 的严格旁路入口。它不替换线上恢复入口，只额外保证：每条磁带动作必须被
    /// HandleAction 接受、稳定等待可取消且传播效果链异常，并在开局和每条动作后逐点回调。
    /// </summary>
    internal static async Task<GameEngine> RebuildForArtifactWorkerAsync(
        string roomId,
        int seed,
        int firstPlayer,
        (string account, string deckRaw) p0,
        (string account, string deckRaw) p1,
        IReadOnlyList<ActionEntry> actions,
        int stableTimeoutMilliseconds,
        Action<GameEngine>? configureEngine,
        Func<SettledReplayPoint, CancellationToken, ValueTask> onSettled,
        CancellationToken cancellationToken,
        bool leaderKeywordWildcard = false,
        bool p0AlwaysPrompt = false,
        bool p1AlwaysPrompt = false,
        bool openingSetupAfterFirstPlayerChoice = false,
        CardRuleset? ruleset = null)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(onSettled);
        if (stableTimeoutMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(stableTimeoutMilliseconds),
                "稳定等待超时必须大于 0 毫秒");

        cancellationToken.ThrowIfCancellationRequested();
        var engine = new GameEngine(
            roomId,
            ("artifact-replay-p0", p0.account, p0.deckRaw),
            ("artifact-replay-p1", p1.account, p1.deckRaw),
            firstPlayer: firstPlayer,
            rngSeed: seed,
            leaderKeywordWildcard: leaderKeywordWildcard,
            deferOpeningSetupUntilFirstPlayerChosen: openingSetupAfterFirstPlayerChoice,
            deferInitialSetupUntilStart: openingSetupAfterFirstPlayerChoice,
            ruleset: ruleset);

        configureEngine?.Invoke(engine);
        engine.State.Players[0].AlwaysPromptOnLifeReveal = p0AlwaysPrompt;
        engine.State.Players[1].AlwaysPromptOnLifeReveal = p1AlwaysPrompt;
        engine.FlushPendingMatchLogs();

        if (openingSetupAfterFirstPlayerChoice)
            engine.BeginOpeningSequence();

        await engine.WaitSettledForReplayAsync(
            stableTimeoutMilliseconds,
            resolvingPromptId: null,
            cancellationToken);
        await onSettled(new SettledReplayPoint(-1, null, engine), cancellationToken);

        for (var actionIndex = 0; actionIndex < actions.Count; actionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (engine.State.IsGameOver)
                throw new ReplayTapeAfterGameOverException(actionIndex);

            var action = actions[actionIndex];
            var replayData = PrepareReplayData(action);
            var resolvingPromptId = string.Equals(action.Action, "PromptResponse", StringComparison.Ordinal)
                ? engine.State.PendingPrompt?.PromptId
                : null;
            if (!engine.HandleAction(action.PlayerIndex, action.Action, replayData, source: action.Source))
                throw new ReplayActionRejectedException(actionIndex, action);

            await engine.WaitSettledForReplayAsync(
                stableTimeoutMilliseconds,
                resolvingPromptId,
                cancellationToken);
            RestoreHexDraftDeadline(engine, action);
            await onSettled(
                new SettledReplayPoint(actionIndex, action, engine),
                cancellationToken);
        }

        return engine;
    }

    private static JsonElement PrepareReplayData(ActionEntry entry)
    {
        if (!string.Equals(entry.Action, "RequestDraw", StringComparison.Ordinal))
            return entry.Data;

        if (entry.Data.ValueKind != JsonValueKind.Object
            || entry.Data.TryGetProperty("description", out _))
            return entry.Data;

        // 旧协议的 RequestDraw payload 是空对象；迁移仅覆盖“字段不存在”。
        // 非文字、空白或超长字段表示日志已损坏，不得冒充旧协议申请而被接受。
        return JsonSerializer.SerializeToElement(new
        {
            description = GameState.LegacyDrawRequestDescription,
        });
    }

    private static void RestoreHexDraftDeadline(GameEngine engine, ActionEntry action)
    {
        if (action.HexDraftRoundId is null || action.HexDraftDeadlineUtc is null) return;
        var draft = engine.State.HexState.ActiveDraft;
        if (draft is not null && draft.RoundId == action.HexDraftRoundId)
            draft.DeadlineUtc = action.HexDraftDeadlineUtc.Value.ToUniversalTime();
    }
}
