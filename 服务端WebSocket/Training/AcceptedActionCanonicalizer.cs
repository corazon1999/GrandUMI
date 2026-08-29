using System.Text.Json;

namespace GrandUMI.Training;

/// <summary>accepted 动作在线日志与离线磁带共用的冻结规范描述。</summary>
internal static class AcceptedActionCanonicalizer
{
    // 该集合属于历史 accepted tape v1 的哈希语义；不可用新动作空间替换。
    // 新训练数据集由 TrainingDatasetMatchCollector 另行使用 LegalActionSpace 门禁。
    private static readonly HashSet<string> StrategyActions = new(StringComparer.Ordinal)
    {
        "ChooseFirstPlayer",
        "Mulligan",
        "PlayCard",
        "AttachDon",
        "UndoAttachDon",
        "Attack",
        "DeclareBlocker",
        "PassBlock",
        "PlayCounter",
        "PassCounter",
        "EndTurn",
        "PromptResponse",
        "UseEffect",
    };

    public static AcceptedActionCanonicalDescriptor Create(
        long orderSeq,
        long sourceSeq,
        long? resultSeq,
        int actorSeat,
        string action,
        JsonElement data,
        ReplayActionSource source)
    {
        var normalized = CanonicalJson.NormalizeObject(data);
        var isTrainingLabelCandidate = source == ReplayActionSource.Player
            && StrategyActions.Contains(action);
        var canonical = JsonSerializer.SerializeToElement(new
        {
            orderSeq,
            sourceSeq,
            resultSeq,
            actorSeat,
            action,
            data = normalized,
            source = source.ToString().ToLowerInvariant(),
            isTrainingLabelCandidate,
        });
        return new AcceptedActionCanonicalDescriptor(
            orderSeq,
            sourceSeq,
            resultSeq,
            actorSeat,
            action,
            normalized,
            source,
            isTrainingLabelCandidate,
            CanonicalJson.Hash(canonical));
    }
}

internal sealed record AcceptedActionCanonicalDescriptor(
    long OrderSeq,
    long SourceSeq,
    long? ResultSeq,
    int ActorSeat,
    string Action,
    JsonElement Data,
    ReplayActionSource Source,
    bool IsTrainingLabelCandidate,
    string StableHash);
