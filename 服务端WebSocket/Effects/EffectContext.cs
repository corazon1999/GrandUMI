using GrandUMI.Game;

namespace GrandUMI.Effects;

/// <summary>
/// 效果执行上下文：当前正在解决某张卡的效果时，引擎填充此结构传给脚本
/// </summary>
public class EffectContext
{
    public required GameState State { get; init; }
    public required int OwnerIndex   { get; init; }   // 效果所属玩家
    public required CardInstance Source { get; init; } // 效果源卡（即"自身"）
    public EffectTrigger Trigger    { get; init; }
    public Dictionary<string, object?> Vars { get; init; } = new(); // DSL 变量绑定 / 手写卡自由用
    public List<GameEvent> Events   { get; init; } = new();
    public required IPromptService Prompts { get; init; }

    /// <summary>引擎引用，DSL 中某些 op（如 OpponentDiscard）需要发起对手 Prompt</summary>
    public GameEngine? Engine { get; init; }
}

public class GameEvent
{
    public required string Kind { get; init; }
    public Dictionary<string, object?> Payload { get; init; } = new();
}
