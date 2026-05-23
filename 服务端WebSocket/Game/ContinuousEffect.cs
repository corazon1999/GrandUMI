namespace GrandUMI.Game;

/// <summary>
/// 永续效果（在条件成立期间持续生效的力量修正）
/// 由领航/角色/舞台卡注册（手写脚本或 DSL passive 节），引擎在每次 CurrentPower 评估时累加。
///
/// 典型用例：
///   【咚!!×1】【对方回合中】我方所有 X 力量 +N
///   规则上，我方所有拥有《Y》特征的领袖/角色力量 +M
/// </summary>
public class ContinuousEffect
{
    public required string SourceCardId { get; init; }    // 来源卡 GUID（来源 KO/离场后失效）
    public required ContinuousScope Scope { get; init; }
    public int PowerDelta { get; init; }

    /// <summary>
    /// 评估当前是否激活：
    ///   sideMask = bit0:我方激活 bit1:对方激活；
    ///   需要 OnlyOwnerTurn / OnlyOpponentTurn 等限制时按 turnPlayer 判断
    /// </summary>
    public required Func<GameState, int, CardInstance, bool> Predicate { get; init; }
}

public class ContinuousScope
{
    /// <summary>受影响哪一方：0=源卡的同方，1=源卡的对方，-1=双方</summary>
    public int Side { get; init; }
    public bool IncludeLeader { get; init; } = true;
    public bool IncludeCharacters { get; init; } = true;
    public Func<CardInstance, bool>? Filter { get; init; }
}
