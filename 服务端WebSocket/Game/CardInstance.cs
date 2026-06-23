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

    /// <summary>原本力量修正（"原本的力量变为 X"）</summary>
    public int? OriginalPowerOverride { get; set; }

    /// <summary>效果是否被无效化（OnXxx 触发被跳过）</summary>
    public bool IsEffectsNullified { get; set; }

    /// <summary>临时限制（CannotAttack/CannotBeKOd/CannotBeBlocker/CannotBeChosen）</summary>
    public List<CardRestriction> Restrictions { get; } = new();

    /// <summary>本回合无法攻击对方"原本费用 ≤ 此值"的角色（0=无限制，OP12-020）</summary>
    public int NoAttackCostLeThisTurn { get; set; }

    /// <summary>本回合中此卡与对方角色战斗后转为活跃状态（OP12-020）</summary>
    public bool ReactivateAfterBattleThisTurn { get; set; }

    /// <summary>卡名替身（"卡牌名也视为 X"），过滤 nameEquals 时同时匹配主名与别名</summary>
    public List<string> NameAliases { get; } = new();

    public bool MatchesName(string name)
        => Info.Name == name || NameAliases.Contains(name);

    /// <summary>当前费用（含修正，不低于 0）</summary>
    public int CurrentCost()
    {
        int v = Info.Cost + CostModThisTurn + CostModPersistent;
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
        int baseP = OriginalPowerOverride ?? Info.Power;
        int donBonus = ownerTurn ? donAttachedCount * 1000 : 0;
        return baseP + donBonus + PowerModThisTurn + PowerModThisBattle + PowerModPersistent;
    }
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
