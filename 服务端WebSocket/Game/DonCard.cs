namespace GrandUMI.Game;

public enum DonState
{
    InDeck    = 0,  // 在咚!!卡组中
    Active    = 1,  // 费用区·活跃（竖置）
    Rest      = 2,  // 费用区·休息（横置）
    Attached  = 3,  // 赋予中（不属于活跃也不属于休息）
}

public class DonCard
{
    public Guid Id { get; } = Guid.NewGuid();
    public DonState State { get; set; } = DonState.InDeck;
    /// <summary>仅 Attached 时有效：被赋予的目标</summary>
    public Guid? AttachedToCardId { get; set; }
}
