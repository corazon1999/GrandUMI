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

    /// <summary>持续费用修正（影响 KO/选择判定与卡面显示，正数表示费用升高）</summary>
    public int CostDelta { get; init; }

    /// <summary>持续/条件赋予的关键词（如 "速攻"/"阻挡者"/"双重攻击"/"不可阻挡"/"流放"）；
    /// 非空时 Predicate 成立期间使 scope 内卡牌视为拥有该关键词（由 ActionValidator.HasKeyword 查询）。</summary>
    public string? GrantKeyword { get; init; }

    /// <summary>持续"不会被KO"保护；非空时 Predicate 成立期间 scope 内卡牌不会被 KO。
    /// "battle"=仅战斗中, "effect"=仅因效果, "any"=任何 KO。</summary>
    public string? KoGuard { get; init; }

    /// <summary>持续"不会离开场上"保护（含KO/退回手牌/放回卡组/置入生命等离场）；非空时 Predicate 成立期间
    /// scope 内卡牌不会因相应来源离场。"effect"=仅因效果离场, "any"=任何离场。比 KoGuard 范围更广。</summary>
    public string? LeaveGuard { get; init; }

    /// <summary>持续"效果无效"；true 时 Predicate 成立期间 scope 内卡牌效果被无效化。</summary>
    public bool NullifyEffect { get; init; }

    /// <summary>仅无效化某一类触发（如仅【登场时】OnEnterField）；非空时 Predicate 成立期间 scope 内卡牌该触发不发动。</summary>
    public Effects.EffectTrigger? NullifyOnlyTrigger { get; init; }

    /// <summary>持续"无法转为活跃"；true 时 Predicate 成立期间 scope 内卡牌在重置阶段跳过激活。</summary>
    public bool PreventReset { get; init; }

    /// <summary>持续/条件施加的限制（如条件性 CannotAttack）；非空时 Predicate 成立期间 scope 内卡牌视为拥有该限制，
    /// 由 ActionValidator 等查询（区别于一次性的 CardInstance.Restrictions）。</summary>
    public RestrictionKind? GrantRestriction { get; init; }

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
