using GrandUMI.Cards;

namespace GrandUMI.Game.PhaseFlow;

/// <summary>
/// 战斗 5 步骤：Attack → Block → Counter → Damage → BattleEnd
/// </summary>
public static class BattleEngine
{
    /// <summary>攻击宣言</summary>
    public static void StartAttack(GameState s, Guid attackerId, bool targetIsLeader, Guid? targetId)
    {
        var atkPlayer = s.CurrentTurnPlayer;
        var defPlayer = 1 - atkPlayer;

        // 攻击者横置（领袖或角色）
        var me = s.Players[atkPlayer];
        CardInstance attacker = (me.Leader.Id == attackerId) ? me.Leader
            : me.Characters.First(c => c.Id == attackerId);
        attacker.IsTapped = true;

        s.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = atkPlayer,
            DefenderPlayerIndex = defPlayer,
            AttackerCardId = attackerId,
            TargetIsLeader = targetIsLeader,
            TargetCardId = targetIsLeader ? null : targetId,
        };
        s.Phase = Phase.BattleAttack;
        // M3 起：触发【攻击时】/【对方的攻击时】效果
        // 现在直接进入 Block 步骤
        s.Phase = Phase.BattleBlock;
    }

    /// <summary>防守方宣言阻挡者</summary>
    public static void DeclareBlocker(GameState s, Guid blockerId)
    {
        var b = s.CurrentBattle!;
        b.BlockerDeclared = true;
        b.ReplacedByBlockerCardId = blockerId;
        // 阻挡者会变为新的目标，原目标释放，阻挡者横置
        var defender = s.Players[b.DefenderPlayerIndex];
        var blocker = defender.Characters.First(c => c.Id == blockerId);
        blocker.IsTapped = true;
        b.TargetIsLeader = false;
        b.TargetCardId = blockerId;
        s.Phase = Phase.BattleCounter;
    }

    /// <summary>防守方放弃阻挡</summary>
    public static void PassBlock(GameState s)
    {
        s.Phase = Phase.BattleCounter;
    }

    /// <summary>反击：手牌中的反击事件加力量；图标反击（角色卡counter+N）</summary>
    public static void ApplyCounter(GameState s, int defenderIdx, int counterValue)
    {
        var b = s.CurrentBattle!;
        b.DefenderBattleBonus += counterValue;
    }

    /// <summary>防守方放弃反击 → 进入伤害步骤（同步部分），返回需异步处理的领袖伤害量</summary>
    public static int PassCounter(GameState s)
    {
        s.Phase = Phase.BattleDamage;
        return ResolveDamageSync(s);
    }

    /// <summary>同步部分伤害结算 + 标记需要造成的领袖伤害（由 GameEngine 异步处理生命牌触发）</summary>
    public static int ResolveDamageSync(GameState s)
    {
        var b = s.CurrentBattle!;
        var atk = s.Players[b.AttackerPlayerIndex];
        var def = s.Players[b.DefenderPlayerIndex];

        var attacker = atk.Leader.Id == b.AttackerCardId ? atk.Leader
            : atk.Characters.First(c => c.Id == b.AttackerCardId);
        int attackerPower = attacker.CurrentPower(atk.AttachedDonCount(attacker.Id), ownerTurn: true) + b.AttackerBattleBonus;

        bool attackerWins;
        int leaderDamage = 0;

        if (b.TargetIsLeader)
        {
            int defenderPower = def.Leader.CurrentPower(def.AttachedDonCount(def.Leader.Id), ownerTurn: false) + b.DefenderBattleBonus;
            attackerWins = attackerPower >= defenderPower;
            if (attackerWins)
                leaderDamage = Validation.ActionValidator.HasKeyword(attacker, "双重攻击") ? 2 : 1;
        }
        else
        {
            var target = def.Characters.First(c => c.Id == b.TargetCardId);
            int defenderPower = target.CurrentPower(def.AttachedDonCount(target.Id), ownerTurn: false) + b.DefenderBattleBonus;
            attackerWins = attackerPower >= defenderPower;
            if (attackerWins) KOCard(s, b.DefenderPlayerIndex, target);
        }
        return leaderDamage;
    }

    public static void EndBattle(GameState s)
    {
        // 清除战斗内的临时修正
        foreach (var p in s.Players)
        {
            foreach (var c in p.Characters) { c.PowerModThisBattle = 0; }
            p.Leader.PowerModThisBattle = 0;
            // 战斗结束时关键字清理
            foreach (var c in p.Characters) c.GainedKeywords.RemoveAll(k => k.Duration == KeywordDuration.ThisBattle);
            p.Leader.GainedKeywords.RemoveAll(k => k.Duration == KeywordDuration.ThisBattle);
        }
        s.CurrentBattle = null;
        s.Phase = Phase.Main;
    }

    /// <summary>把一张角色 KO（场上 → 废弃区），归还其附着的咚</summary>
    public static void KOCard(GameState s, int ownerIdx, CardInstance card)
    {
        var p = s.Players[ownerIdx];
        // 归还附着咚 → 休息状态放回费用区
        foreach (var d in p.CostArea)
        {
            if (d.State == DonState.Attached && d.AttachedToCardId == card.Id)
            {
                d.State = DonState.Rest;
                d.AttachedToCardId = null;
            }
        }
        p.Characters.Remove(card);
        p.Trash.Add(card);
        // M3 起：触发【KO时】效果
    }
}
