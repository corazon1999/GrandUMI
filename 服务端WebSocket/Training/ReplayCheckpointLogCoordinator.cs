using System.Text.Json;
using GrandUMI.Game;
using GrandUMI.Game.Logging;

namespace GrandUMI.Training;

internal static class ReplayCheckpointFeature
{
    public const string EnvironmentVariable = "GRANDUMI_REPLAY_CHECKPOINT_LOG";

    public static bool IsEnabled()
        => IsEnabled(Environment.GetEnvironmentVariable(EnvironmentVariable));

    internal static bool IsEnabled(string? value)
        => string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 只允许房间单读者在稳定点调用。它持有本进程内随机轨迹，不尝试跨重启猜测连续性。
/// </summary>
internal sealed class ReplayCheckpointLogCoordinator
{
    public const string StatusSchema = "grandumi.replay_checkpoint_status.v1";

    private readonly object _gate = new();
    private readonly string _matchId;
    private readonly IReplayCheckpointProvider _provider;
    private readonly List<ReplayRandomTraceEvent> _randomTrace = new();
    private bool _openingWritten;
    private bool _terminalWritten;
    private bool _disabled;
    private int _actionCount;
    private long _lastActionOrderSeq;

    public ReplayCheckpointLogCoordinator(
        string matchId,
        IReplayCheckpointProvider? provider = null)
    {
        if (string.IsNullOrWhiteSpace(matchId))
            throw new ArgumentException("matchId 不能为空", nameof(matchId));
        _matchId = matchId;
        _provider = provider ?? DeterministicReplayCheckpointProvider.Current;
    }

    public bool IsDisabled
    {
        get { lock (_gate) return _disabled; }
    }

    /// <summary>在日志 receipt 已返回后观察事件；未入队或随机 payload 不可规范化时立即停用。</summary>
    public void Observe(
        GameState state,
        string kind,
        int? actor,
        object? payload,
        MatchLogAppendReceipt receipt)
    {
        lock (_gate)
        {
            if (_disabled) return;
            if (!receipt.Queued)
            {
                DisableLocked(state, "match_log_append_failed");
                return;
            }
            if (!string.Equals(kind, "random_event", StringComparison.Ordinal)) return;
            try
            {
                _randomTrace.Add(new ReplayRandomTraceEvent(
                    actor,
                    JsonSerializer.SerializeToElement(payload ?? new { }).Clone()));
            }
            catch
            {
                DisableLocked(state, "random_event_not_canonical");
            }
        }
    }

    public bool WriteOpening(GameEngine engine)
    {
        lock (_gate)
        {
            if (_disabled || _terminalWritten) return false;
            if (_openingWritten) return true;
            return WriteCheckpointLocked(
                engine,
                new ReplayCheckpointContext(ReplayCheckpointPosition.Opening, -1, null, null));
        }
    }

    public bool WriteAfterAction(GameEngine engine, AcceptedActionLogReceipt? action)
    {
        lock (_gate)
        {
            if (_disabled || _terminalWritten) return false;
            if (!_openingWritten)
            {
                DisableLocked(engine.State, "opening_checkpoint_missing");
                return false;
            }
            if (action is null || !action.Queued)
            {
                DisableLocked(engine.State, "accepted_action_log_missing");
                return false;
            }
            if (action.OrderSeq <= _lastActionOrderSeq)
            {
                DisableLocked(engine.State, "accepted_action_order_not_monotonic");
                return false;
            }

            var context = new ReplayCheckpointContext(
                ReplayCheckpointPosition.AfterAction,
                _actionCount,
                action.OrderSeq,
                action.StableHash);
            if (!WriteCheckpointLocked(engine, context)) return false;
            _lastActionOrderSeq = action.OrderSeq;
            _actionCount++;
            return true;
        }
    }

    public bool WriteTerminal(GameEngine engine)
    {
        lock (_gate)
        {
            if (_disabled) return false;
            if (_terminalWritten) return true;
            if (!_openingWritten)
            {
                DisableLocked(engine.State, "opening_checkpoint_missing_at_terminal");
                return false;
            }
            var written = WriteCheckpointLocked(
                engine,
                new ReplayCheckpointContext(
                    ReplayCheckpointPosition.Terminal,
                    _actionCount,
                    null,
                    null));
            if (written) _terminalWritten = true;
            return written;
        }
    }

    public void Disable(GameState state, string reason)
    {
        lock (_gate) DisableLocked(state, reason);
    }

    private bool WriteCheckpointLocked(GameEngine engine, ReplayCheckpointContext context)
    {
        try
        {
            var digest = _provider.Capture(engine, context, _randomTrace.AsReadOnly());
            var payload = new
            {
                schema = ReplayCheckpointContractParser.Schema,
                position = ReplayCheckpointContractParser.PositionName(context.Position),
                actionOrderSeq = context.ActionOrderSeq,
                actionStableHash = context.ActionStableHash,
                stateDigest = digest.StateDigest,
                publicStateDigest = digest.PublicStateDigest,
                randomTraceDigest = digest.RandomTraceDigest,
                randomEventCount = digest.RandomEventCount,
            };
            var receipt = MatchLogRecorder.AppendRequired(
                _matchId,
                engine.State,
                "replay_checkpoint",
                -1,
                payload);
            if (!receipt.Queued)
            {
                DisableLocked(engine.State, "checkpoint_append_failed");
                return false;
            }
            if (context.Position == ReplayCheckpointPosition.Opening)
                _openingWritten = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ReplayCheckpoint] {_matchId} 写入失败：{ex.Message}");
            DisableLocked(engine.State, "checkpoint_capture_or_append_failed");
            return false;
        }
    }

    private void DisableLocked(GameState state, string reason)
    {
        if (_disabled) return;
        _disabled = true;
        try
        {
            MatchLogRecorder.AppendRequired(_matchId, state, "replay_checkpoint_status", -1, new
            {
                schema = StatusSchema,
                enabled = false,
                reason,
                completedActionCheckpoints = _actionCount,
                randomEventCount = _randomTrace.Count,
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ReplayCheckpoint] {_matchId} 停用标记写入失败：{ex.Message}");
        }
    }
}
