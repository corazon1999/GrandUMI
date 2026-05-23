using GrandUMI.Cards;

namespace GrandUMI.Game;

/// <summary>
/// 单方玩家在对战中的完整状态
/// </summary>
public class PlayerState
{
    public required string SessionId   { get; set; }
    public required string AccountName { get; set; }

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

    // ── 帮助查询 ──────────────────────────────────────────────────────────

    public int ActiveDonCount => CostArea.Count(d => d.State == DonState.Active);
    public int RestDonCount   => CostArea.Count(d => d.State == DonState.Rest);
    public int AttachedDonCount(Guid cardId) => CostArea.Count(d => d.State == DonState.Attached && d.AttachedToCardId == cardId);
    public int TotalDonInCostArea => CostArea.Count;
    public int DeckCount => Deck.Count;
    public int LifeCount => LifeArea.Count;
}
