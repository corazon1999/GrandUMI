using GrandUMI.Cards;

namespace GrandUMI.Game;

/// <summary>
/// 场上/手牌/卡组/废弃区中具体一张卡的实例（vs CardInfo 静态卡数据）
/// 通过 Id 区分同名卡的多个实例
/// </summary>
public class CardInstance
{
    public Guid Id { get; } = DeterministicId.Next();
    public required CardInfo Info { get; init; }

    /// <summary>是否横置（领航/角色）</summary>
    public bool IsTapped { get; set; }

    /// <summary>本回合内的临时力量修正（回合结束清零）</summary>
    public int PowerModThisTurn { get; set; }

    /// <summary>本次战斗内的临时力量修正（战斗结束清零）</summary>
    public int PowerModThisBattle { get; set; }

    /// <summary>持久力量修正</summary>
    public int PowerModPersistent { get; set; }

    /// <summary>"直到下个对方结束阶段结束时为止"持续的力量修正（由 TurnEngine 在对方结束阶段清除）。
    /// PowerModThisTurn 在施加者本回合末就清(过早)、PowerModPersistent 永不清(过久)，故另设此通道。</summary>
    public List<CardPowerMod> PowerModsUntilOppEnd { get; } = new();

    /// <summary>
    /// “直到下个我方回合开始时为止”的力量修正。它与“直到对方结束阶段”不是同一期限：
    /// 对方结束阶段完成后仍保留，进入记录方下一个回合的准备阶段时才精确清除。
    /// </summary>
    public List<PowerModUntilNextOwnTurnStart> PowerModsUntilNextOwnTurnStart { get; } = new();

    /// <summary>临时获得的关键字（带过期时点）</summary>
    public List<TemporaryKeyword> GainedKeywords { get; } = new();

    /// <summary>是否被标记"下个我方重置阶段不会转活跃"</summary>
    public bool CannotActivateNextReset { get; set; }

    /// <summary>该卡在生命区时是否正面朝上（默认背面 false；【翻至正面朝上】类效果置 true，离开生命区时重置）</summary>
    public bool IsLifeFaceUp { get; set; }

    /// <summary>登场回合编号（用于速攻判断：新登场角色当回合不能攻击除非有速攻）</summary>
    public int TurnPlayed { get; set; }

    /// <summary>本回合"每回合1次"效果是否已用</summary>
    public HashSet<string> OncePerTurnUsedKeys { get; } = new();

    // ── B 阶段 P1 新增字段 ──────────────────────────────────────────────

    /// <summary>本回合费用修正（在手牌时影响打出费用 / 在场上影响 KO 判定）</summary>
    public int CostModThisTurn { get; set; }

    /// <summary>持续费用修正（直到效果失效）</summary>
    public int CostModPersistent { get; set; }

    /// <summary>
    /// 跟随卡牌实体跨区域保留的永久费用修正。当前仅由海克斯“物法皆修”写入，
    /// 不属于离场应清除的场上持续修正，因此 ResetCardEphemeralState 不得重置。
    /// </summary>
    public int EntityCostModPersistent { get; set; }

    /// <summary>以下标记只在本次留在角色区期间有效，由 CharacterZone 离场清理统一重置。</summary>
    public bool HexEnteredFromTrash { get; set; }
    public bool HexEnteredFromHandByEffect { get; set; }
    public bool HexThreeAdmiralsGranted { get; set; }
    public int HexHighCostEntryTurn { get; set; }

    /// <summary>“直到下个对方结束阶段结束时为止”的费用修正。
    /// 数值同时计入 CostModPersistent，此列表负责记录到期方与精确回收的增量。</summary>
    public List<CardCostMod> CostModsUntilOppEnd { get; } = new();

    private int? _originalPowerOverride;

    /// <summary>
    /// 本回合的“原本力量变为 X”。同一时点有多条此类效果时规则取最高值；
    /// 因而重复写入只提升当前聚合值，写入 null 才在回合清理或离场重置时清空。
    /// </summary>
    public int? OriginalPowerOverride
    {
        get => _originalPowerOverride;
        set => _originalPowerOverride = value.HasValue && _originalPowerOverride.HasValue
            ? Math.Max(_originalPowerOverride.Value, value.Value)
            : value;
    }

    /// <summary>“原本力量变为X，直到下个对方结束阶段”为止的跨回合覆盖。</summary>
    public List<OriginalPowerOverrideUntilOppEnd> OriginalPowerOverridesUntilOppEnd { get; } = new();

    /// <summary>当前实例上所有仍有效的“原本力量变为 X”效果的最高值；没有效果时为 null。</summary>
    internal int? HighestInstanceOriginalPowerOverride
    {
        get
        {
            int? highest = OriginalPowerOverride;
            foreach (var change in OriginalPowerOverridesUntilOppEnd)
                highest = highest.HasValue ? Math.Max(highest.Value, change.Value) : change.Value;
            return highest;
        }
    }

    /// <summary>效果是否被无效化（OnXxx 触发被跳过）</summary>
    public bool IsEffectsNullified { get; set; }

    /// <summary>临时限制（CannotAttack/CannotBeKOd/CannotBeBlocker/CannotBeChosen）</summary>
    public List<CardRestriction> Restrictions { get; } = new();

    /// <summary>本回合无法攻击对方"原本费用 ≤ 此值"的角色（0=无限制，OP12-020）</summary>
    public int NoAttackCostLeThisTurn { get; set; }

    /// <summary>本回合中此卡是否已经与对方角色进行过战斗（OP12-020）</summary>
    public bool BattledOpponentCharacterThisTurn { get; set; }

    /// <summary>卡名替身（"卡牌名也视为 X"），过滤 nameEquals 时同时匹配主名与别名</summary>
    public List<string> NameAliases { get; } = new();

    /// <summary>本回合临时获得的属性（例如 OP15-093 赋予“斩”）。</summary>
    public HashSet<string> GainedPropertiesThisTurn { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 目标卡记录本次留在角色区期间，被哪些“建立时快照”持续效果选中；
    /// 来源卡记录自身 ID，表示本次留场已经建立快照。双方离场时均由区域入口统一清理。
    /// </summary>
    public HashSet<Guid> FieldSnapshotSourceIds { get; } = new();

    public bool HasProperty(string property)
        => Info.Property.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains(property)
            || GainedPropertiesThisTurn.Contains(property);

    public string CurrentProperty
        => string.Join('/', Info.Property.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Concat(GainedPropertiesThisTurn).Distinct(StringComparer.Ordinal));

    public bool HasAnyProperty => !string.IsNullOrEmpty(CurrentProperty);

    public bool MatchesName(string name)
        => Info.NameIs(name) || NameAliases.Contains(name);  // 静态视为别名(Info.AlsoNames) + 运行时别名

    /// <summary>当前费用（含修正，不低于 0）</summary>
    public int CurrentCost()
    {
        int v = Info.Cost + CostModThisTurn + CostModPersistent + EntityCostModPersistent;
        return v < 0 ? 0 : v;
    }

    public bool HasRestriction(RestrictionKind kind)
    {
        foreach (var r in Restrictions) if (r.Kind == kind) return true;
        return false;
    }

    /// <summary>当前总力量（含修正，可为负）</summary>
    public int CurrentPower(int donAttachedCount, bool ownerTurn)
    {
        int baseP = HighestInstanceOriginalPowerOverride ?? Info.Power;
        int donBonus = ownerTurn ? donAttachedCount * 1000 : 0;
        int untilOppEnd = 0;
        foreach (var m in PowerModsUntilOppEnd) untilOppEnd += m.Delta;
        int untilNextOwnTurn = 0;
        foreach (var m in PowerModsUntilNextOwnTurnStart) untilNextOwnTurn += m.Delta;
        return baseP + donBonus + PowerModThisTurn + PowerModThisBattle + PowerModPersistent
            + untilOppEnd + untilNextOwnTurn;
    }
}

/// <summary>持续到记录方下个回合开始时的单次力量修正。</summary>
public sealed class PowerModUntilNextOwnTurnStart
{
    public required int Delta { get; init; }

    /// <summary>“我方”所指的玩家；期限只会在该玩家的回合开始时到期。</summary>
    public required int OwnerSide { get; init; }

    /// <summary>
    /// 施加时的全局回合编号。准备阶段可能因重连/恢复被重复进入；只有回合编号严格推进后才可清除，
    /// 避免同一回合的重复入口把刚建立的期限误判为到期。
    /// </summary>
    public required int AppliedTurnCount { get; init; }
}

/// <summary>持续到施加方下个对方结束阶段的原本力量覆盖。</summary>
public class OriginalPowerOverrideUntilOppEnd
{
    public required int Value { get; init; }
    public int AppliedBySide { get; init; } = -1;
    public int EndPhasesSeen { get; set; }
}

/// <summary>"直到下个对方结束阶段"持续的单次力量修正。清除规则同关键字/限制：仅在"对方"(1-AppliedBySide)的结束阶段清。</summary>
public class CardPowerMod
{
    public required int Delta { get; init; }
    /// <summary>施加此修正的控制者一方。-1 表示未指定，回退为"生存一个结束阶段"。</summary>
    public int AppliedBySide { get; init; } = -1;
    public int EndPhasesSeen { get; set; }
}

/// <summary>持续到施加方下个对方结束阶段的单次费用修正。</summary>
public class CardCostMod
{
    public required int Delta { get; init; }
    /// <summary>施加此修正的一方。-1 表示旧调用方未指定，回退为“生存一个结束阶段”。</summary>
    public int AppliedBySide { get; init; } = -1;
    public int EndPhasesSeen { get; set; }
}

public enum RestrictionKind
{
    CannotAttack,
    CannotBeKOd,
    CannotBeBlocker,
    CannotBeChosen,
    CannotBeRested,   // 无法被(效果)转为休息状态
}

public class CardRestriction
{
    public required RestrictionKind Kind { get; init; }
    public required KeywordDuration Duration { get; init; }
    /// <summary>施加此限制的控制者一方（用于 UntilNextOpponentEndPhase：仅在"对方"的结束阶段清除）。-1 表示未指定，回退为"生存一个结束阶段"。</summary>
    public int AppliedBySide { get; init; } = -1;
    /// <summary>UntilNextOpponentEndPhase 在 AppliedBySide 未指定时，已经历的结束阶段数（生存 1 个）。</summary>
    public int EndPhasesSeen { get; set; }
}

public class TemporaryKeyword
{
    public required string Keyword { get; init; } // "速攻" / "双重攻击" / "阻挡者" / "不可阻挡"
    public required KeywordDuration Duration { get; init; }
    /// <summary>赋予此关键词的控制者一方（用于 UntilNextOpponentEndPhase：仅在"对方"的结束阶段清除）。-1 表示未指定，回退为"生存一个结束阶段"。</summary>
    public int AppliedBySide { get; init; } = -1;
    /// <summary>UntilNextOpponentEndPhase 在 AppliedBySide 未指定时，已经历的结束阶段数（生存 1 个）。</summary>
    public int EndPhasesSeen { get; set; }
}

public enum KeywordDuration
{
    ThisTurn,
    ThisBattle,
    UntilNextOpponentEndPhase,
}
