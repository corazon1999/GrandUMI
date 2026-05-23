using GrandUMI.Cards;

namespace GrandUMI.Game;

/// <summary>
/// 场上/手牌/卡组/废弃区中具体一张卡的实例（vs CardInfo 静态卡数据）
/// 通过 Id 区分同名卡的多个实例
/// </summary>
public class CardInstance
{
    public Guid Id { get; } = Guid.NewGuid();
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

    /// <summary>登场回合编号（用于速攻判断：新登场角色当回合不能攻击除非有速攻）</summary>
    public int TurnPlayed { get; set; }

    /// <summary>本回合"每回合1次"效果是否已用</summary>
    public HashSet<string> OncePerTurnUsedKeys { get; } = new();

    /// <summary>当前总力量（含修正，可为负）</summary>
    public int CurrentPower(int donAttachedCount, bool ownerTurn)
    {
        int baseP = Info.Power;
        int donBonus = ownerTurn ? donAttachedCount * 1000 : 0;
        return baseP + donBonus + PowerModThisTurn + PowerModThisBattle + PowerModPersistent;
    }
}

public class TemporaryKeyword
{
    public required string Keyword { get; init; } // "速攻" / "双重攻击" / "阻挡者" / "不可阻挡"
    public required KeywordDuration Duration { get; init; }
}

public enum KeywordDuration
{
    ThisTurn,
    ThisBattle,
    UntilNextOpponentEndPhase,
}
