namespace GrandUMI.Game;

/// <summary>
/// 回合内的 5 个阶段
/// </summary>
public enum Phase
{
    Reset = 0,   // 重置阶段
    Draw  = 1,   // 抽卡阶段
    Don   = 2,   // 咚!!阶段
    Main  = 3,   // 主要阶段
    End   = 4,   // 结束阶段
    // 战斗子流程，主要阶段中的暂态
    BattleAttack  = 10,
    BattleBlock   = 11,
    BattleCounter = 12,
    BattleDamage  = 13,
}

public static class PhaseLabels
{
    public static string Of(Phase p) => p switch
    {
        Phase.Reset         => "Reset",
        Phase.Draw          => "Draw",
        Phase.Don           => "Don",
        Phase.Main          => "Main",
        Phase.End           => "End",
        Phase.BattleAttack  => "Attack",
        Phase.BattleBlock   => "Block",
        Phase.BattleCounter => "Counter",
        Phase.BattleDamage  => "Damage",
        _                    => p.ToString(),
    };
}
