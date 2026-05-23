namespace GrandUMI.Game;

/// <summary>
/// 完整对局状态。引擎所有操作都在此对象上进行。
/// </summary>
public class GameState
{
    public required string RoomId { get; init; }

    /// <summary>双方玩家。约定 0 = 房主/匹配 P1，1 = 加入者/匹配 P2</summary>
    public PlayerState[] Players { get; } = new PlayerState[2];

    /// <summary>当前回合玩家索引（0 / 1）</summary>
    public int CurrentTurnPlayer { get; set; }

    /// <summary>第一回合的先攻方索引</summary>
    public int FirstPlayer { get; init; }

    public int TurnCount { get; set; } = 1;

    public Phase Phase { get; set; } = Phase.Reset;

    /// <summary>等待玩家选择（响应 Prompt）时不为 null</summary>
    public PendingPrompt? PendingPrompt { get; set; }

    /// <summary>当前正在进行的战斗（仅 BattleAttack/Block/Counter/Damage 阶段非 null）</summary>
    public BattleContext? CurrentBattle { get; set; }

    /// <summary>胜负已分时为非空，且 IsGameOver = true</summary>
    public int? WinnerIndex { get; set; }
    public string? GameOverReason { get; set; }
    public bool IsGameOver => WinnerIndex.HasValue;

    /// <summary>序号（每次状态变化 +1，便于客户端识别快照新旧）</summary>
    public int Tick { get; set; }

    /// <summary>
    /// PreKO 触发期间共享的"已拦截 KO"集合。
    /// BattleEngine.KOCardAsync 开始前清空，触发 PreKO 后检查；
    /// 置换效果脚本通过 ctx.State.PreventKO(card) 写入。
    /// </summary>
    public HashSet<Guid> PreventKOCardIds { get; } = new();

    public void MarkPreventKO(Guid cardId) => PreventKOCardIds.Add(cardId);

    /// <summary>永续效果列表（来源卡离场时由 ContinuousEffectRegistry 清理）</summary>
    public List<ContinuousEffect> ContinuousEffects { get; } = new();

    /// <summary>评估指定卡当前从 ContinuousEffects 获得的总力量加成</summary>
    public int ContinuousPowerBonus(int sideIdx, CardInstance card)
    {
        int sum = 0;
        foreach (var eff in ContinuousEffects)
        {
            if (!eff.Predicate(this, sideIdx, card)) continue;
            sum += eff.PowerDelta;
        }
        return sum;
    }

    /// <summary>统一计算某张卡当前力量：基础 + 咚 + 临时修正 + 永续修正</summary>
    public int CurrentPowerOf(int sideIdx, CardInstance card)
    {
        var p = Players[sideIdx];
        int donCount = p.AttachedDonCount(card.Id);
        bool ownerTurn = CurrentTurnPlayer == sideIdx;
        int basePower = card.CurrentPower(donCount, ownerTurn);
        return basePower + ContinuousPowerBonus(sideIdx, card);
    }

    /// <summary>双方都完成 Mulligan 后此值变 true，进入第一回合</summary>
    public bool MulliganBothDone => Players[0].MulliganDone && Players[1].MulliganDone;

    public PlayerState Me(int idx)  => Players[idx];
    public PlayerState Op(int idx)  => Players[1 - idx];
    public PlayerState Turn        => Players[CurrentTurnPlayer];
    public PlayerState NonTurn     => Players[1 - CurrentTurnPlayer];
}

public class PendingPrompt
{
    public required string PromptId      { get; init; }
    public required int    PlayerIndex   { get; init; }   // 等待哪一方响应
    public required string Kind          { get; init; }
    /// <summary>合法选项的 ID 列表（卡 GUID 字符串等）</summary>
    public List<string> ValidChoices     { get; init; } = new();
    public int    MinChoose              { get; init; }
    public int    MaxChoose              { get; init; } = 1;
    public string PromptText             { get; init; } = "";
    /// <summary>用于服务端续接逻辑的回调标识（不下发客户端）</summary>
    public string ResumeKey              { get; init; } = "";
    /// <summary>额外参数（如选项列表的文本描述）</summary>
    public Dictionary<string, object?> Extra { get; init; } = new();
}

public class BattleContext
{
    public required int AttackerPlayerIndex { get; init; }
    /// <summary>攻击者卡实例 ID</summary>
    public required Guid AttackerCardId { get; init; }
    /// <summary>目标：领袖 → null（targetIsLeader=true），角色 → 该卡 ID</summary>
    public Guid? TargetCardId { get; set; }
    public bool TargetIsLeader { get; set; }
    public int DefenderPlayerIndex { get; init; }

    /// <summary>被【阻挡者】替换后的攻击目标 ID（若发生）</summary>
    public Guid? ReplacedByBlockerCardId { get; set; }

    /// <summary>本次战斗已使用过的反击（手牌中事件卡）和反击触发次数</summary>
    public List<Guid> CountersUsed { get; } = new();

    /// <summary>是否已宣言【阻挡者】（每次战斗仅 1 次）</summary>
    public bool BlockerDeclared { get; set; }

    /// <summary>当前战斗的临时威力修正（双方都用此场地）</summary>
    public int AttackerBattleBonus { get; set; }
    public int DefenderBattleBonus { get; set; }
}
