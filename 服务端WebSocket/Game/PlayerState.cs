using GrandUMI.Cards;
using System.Collections.ObjectModel;

namespace GrandUMI.Game;

/// <summary>
/// 角色区集合。角色被移出角色区时统一执行离场清理，避免各效果路径遗漏附着咚的状态恢复。
/// </summary>
public sealed class CharacterZone : Collection<CardInstance>
{
    private readonly Action<CardInstance> _onRemoved;

    internal CharacterZone(Action<CardInstance> onRemoved)
    {
        _onRemoved = onRemoved;
    }

    public void AddRange(IEnumerable<CardInstance> cards)
    {
        foreach (var card in cards) Add(card);
    }

    protected override void RemoveItem(int index)
    {
        var removed = this[index];
        base.RemoveItem(index);
        _onRemoved(removed);
    }

    protected override void SetItem(int index, CardInstance item)
    {
        var removed = this[index];
        base.SetItem(index, item);
        if (!ReferenceEquals(removed, item)) _onRemoved(removed);
    }

    protected override void ClearItems()
    {
        var removed = this.ToList();
        base.ClearItems();
        foreach (var card in removed) _onRemoved(card);
    }
}

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
    public PlayerState()
    {
        Characters = new CharacterZone(RestAttachedDonForDepartingCharacter);
    }

    public required string SessionId   { get; set; }
    public required string AccountName { get; set; }
    /// <summary>对局内公开展示名；登录账号只用于身份校验和内部关联。</summary>
    public string DisplayName { get; set; } = "";
    public string VisibleName => string.IsNullOrWhiteSpace(DisplayName) ? AccountName : DisplayName;
    /// <summary>仅排位对局缓存；创建或恢复房间时读取一次，避免每份快照查询数据库。</summary>
    public PlayerRankIdentity? RankIdentity { get; set; }
    /// <summary>公开外观信息：用于该玩家所有暗置主卡的卡背。</summary>
    public string CardBackId { get; set; } = "classic";
    /// <summary>该玩家卡组公开的异画选择（卡号 → 站内图片路径）。</summary>
    public Dictionary<string, string> SpriteMap { get; } = new(StringComparer.OrdinalIgnoreCase);

    public required CardInstance Leader  { get; init; }
    public List<CardInstance> Hand       { get; } = new();
    /// <summary>角色区（最多 5）</summary>
    public CharacterZone Characters { get; }
    public CardInstance? StageCard       { get; set; }
    /// <summary>仅【三号船坞】存在时启用的第二舞台区。</summary>
    public CardInstance? ExtraStageCard  { get; set; }
    /// <summary>按固定槽位顺序枚举当前舞台；普通模式只会返回首槽。</summary>
    public IEnumerable<CardInstance> StageCards
    {
        get
        {
            if (StageCard is not null) yield return StageCard;
            if (ExtraStageCard is not null) yield return ExtraStageCard;
        }
    }
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

    /// <summary>
    /// 本回合已成功发动过【每回合1次】效果的卡牌实例。
    /// 与 TurnOnceUsed 的内部 key 分离，供快照和界面统一显示“次数仍可用”标识。
    /// </summary>
    public HashSet<Guid> OncePerTurnEffectUsedCardIds { get; } = new();

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

    /// <summary>从其实际舞台槽移除指定实例；重复或错误实例不会改变状态。</summary>
    public bool RemoveStageCard(CardInstance card)
    {
        if (ReferenceEquals(StageCard, card))
        {
            StageCard = null;
            return true;
        }
        if (ReferenceEquals(ExtraStageCard, card))
        {
            ExtraStageCard = null;
            return true;
        }
        return false;
    }

    private void RestAttachedDonForDepartingCharacter(CardInstance character)
    {
        foreach (var don in CostArea)
        {
            if (don.State != DonState.Attached || don.AttachedToCardId != character.Id) continue;
            don.State = DonState.Rest;
            don.AttachedToCardId = null;
        }
    }
}
