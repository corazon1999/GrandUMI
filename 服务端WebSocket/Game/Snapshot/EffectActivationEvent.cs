namespace GrandUMI.Game.Snapshot;

/// <summary>
/// 一张卡进入效果解析时产生的瞬时表现事件。
/// 事件只随下一份状态快照下发，不进入对局持久状态。
/// </summary>
public sealed record EffectActivationEvent(
    int OwnerIndex,
    Guid SourceId,
    string CardNumber,
    string Trigger,
    string ExecutionId);
