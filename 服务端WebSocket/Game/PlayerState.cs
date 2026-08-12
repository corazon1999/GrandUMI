using GrandUMI.Cards;

namespace GrandUMI.Game;

/// <summary>排位对局中公开展示的玩家阵营与开局段位。</summary>
public sealed record PlayerRankIdentity(
    string Faction,
    string Tier,
    int? Division,
    int PlacementGames,
    int PlacementRequired);

/// <summary>
/// 单方玩家在对战中的完整状态
/// </summary>
public class PlayerState
{
    public required string SessionId   { get; set; }
    public required string AccountName { get; set; }
    /// <summary>仅排位对局缓存；创建或恢复房间时读取一次，避免每份快照查询数据库。</summary>
    public PlayerRankIdentity? RankIdentity { get; set; }
    /// <summary>公开外观信息：用于该玩家所有暗置主卡的卡背。</summary>
    public string CardBackId { get; set; } = "classic";
    /// <summary>该玩家卡组公开的异画选择（卡号 → 站内图片路径）。</summary>
    public Dictionary<string, string> SpriteMap { get; } = new(StringComparer.OrdinalIgnoreCase);

    public required CardInstance Leader  { get; init; }
    public List<CardInstance> Hand       { get; } = new();
    /// <summary>角色区（最多 5）</summary>
    public List<CardInstance> Characters { get; } = new();
    public CardInstance? StageCard       { get; set; }
    public List<CardInstance> Trash      { get; } = new();
    public List<CardInstance> Deck       { get; } = new();
    public List<CardInstance> LifeArea   { get; } = new();

    public List<DonCard> DonDeck   { get; } = new();
    public List<DonCard> CostArea  { get; } = new();  // 活跃/休息/赋予中的咚都在这

    /// <summary>是否还有重抽机会</summary>
    public bool HasReDraw { get; set; } = true;
    /// <summary>是否已完成重抽决策</summary>
    public bool MulliganDone { get; set; }

    /// <summary>当前是否打开了"防触发信息泄露"模式</summary>
    public bool AlwaysPromptOnLifeReveal { get; set; }

    /// <summary>每回合 1 次效果的使用记录（key = "卡号-效果Id"）</summary>
    public HashSet<string> TurnOnceUsed { get; } = new();

    /// <summary>本回合中该玩家是否曾因卡牌效果丢弃过手牌（不含发动成本）。</summary>
    public bool HandDiscardedByEffectThisTurn { get; set; }

    /// <summary>本回合中该玩家是否发动过原始费用不低于 3 的事件。</summary>
    public bool HasActivatedBaseCost3PlusEventThisTurn { get; set; }

    // ── 帮助查询 ──────────────────────────────────────────────────────────

    public int ActiveDonCount => CostArea.Count(d => d.State == DonState.Active);
    public int RestDonCount   => CostArea.Count(d => d.State == DonState.Rest);
    public int AttachedDonCount(Guid cardId) => CostArea.Count(d => d.State == DonState.Attached && d.AttachedToCardId == cardId);
    public int TotalDonInCostArea => CostArea.Count;
    public int DeckCount => Deck.Count;
    public int LifeCount => LifeArea.Count;
}
