using GrandUMI.Cards;

namespace GrandUMI.Game.Validation;

/// <summary>
/// 玩家动作的合法性校验（轮到谁、费用、目标是否合法等）
/// 全部为纯检查方法，不修改状态。
/// </summary>
public static class ActionValidator
{
    public record Result(bool Ok, string? Reason);
    private static Result Fail(string reason) => new(false, reason);
    private static readonly Result OkResult = new(true, null);

    public static Result CanEndTurn(GameState s, int playerIdx)
    {
        if (s.CurrentTurnPlayer != playerIdx) return Fail("不是你的回合");
        if (s.Phase != Phase.Main)            return Fail("只能在主要阶段结束回合");
        if (s.CurrentBattle is not null)      return Fail("战斗中不能结束回合");
        return OkResult;
    }

    public static Result CanPlayCard(GameState s, int playerIdx, int handIndex)
    {
        if (s.CurrentTurnPlayer != playerIdx) return Fail("不是你的回合");
        if (s.Phase != Phase.Main)            return Fail("只能在主要阶段出牌");
        if (s.CurrentBattle is not null)      return Fail("战斗中不能出牌");
        var p = s.Players[playerIdx];
        if (handIndex < 0 || handIndex >= p.Hand.Count) return Fail("手牌索引非法");
        var card = p.Hand[handIndex].Info;
        if (card.Kind == CardKind.Leader)     return Fail("领航不能从手牌出");
        if (p.ActiveDonCount < card.Cost)     return Fail($"费用不足，需要 {card.Cost}");
        if (card.Kind == CardKind.Character && p.Characters.Count >= 5)
            return Fail("角色区已满（5）"); // 满员规则：实际可以替换，先简单拒绝
        return OkResult;
    }

    public static Result CanAttachDon(GameState s, int playerIdx, string targetId)
    {
        if (s.CurrentTurnPlayer != playerIdx) return Fail("不是你的回合");
        if (s.Phase != Phase.Main)            return Fail("只能在主要阶段赋予咚");
        if (s.CurrentBattle is not null)      return Fail("战斗中不能赋予咚");
        var p = s.Players[playerIdx];
        if (p.ActiveDonCount < 1)             return Fail("没有活跃咚");
        if (targetId == "leader") return OkResult;
        if (!Guid.TryParse(targetId, out var gid)) return Fail("目标非法");
        if (!p.Characters.Any(c => c.Id == gid))    return Fail("目标不在场上");
        return OkResult;
    }

    public static Result CanAttack(GameState s, int playerIdx, Guid attackerId, bool targetIsLeader, Guid? targetId)
    {
        if (s.CurrentTurnPlayer != playerIdx) return Fail("不是你的回合");
        if (s.Phase != Phase.Main)            return Fail("只能在主要阶段宣言攻击");
        if (s.CurrentBattle is not null)      return Fail("已有战斗进行中");
        if (s.TurnCount == 1)                 return Fail("第 1 回合不能战斗");

        var me = s.Players[playerIdx];
        var op = s.Players[1 - playerIdx];

        // 攻击者：领袖或自己场上角色
        CardInstance attacker;
        if (me.Leader.Id == attackerId) attacker = me.Leader;
        else
        {
            var ch = me.Characters.FirstOrDefault(c => c.Id == attackerId);
            if (ch is null) return Fail("攻击者不在你场上");
            attacker = ch;
        }
        if (attacker.IsTapped) return Fail("攻击者已休息");

        // 新登场角色当回合不能攻击（除非有【速攻】）
        if (attacker != me.Leader && attacker.TurnPlayed == s.TurnCount
            && !HasKeyword(attacker, "速攻"))
            return Fail("新登场角色无法攻击");

        // 目标：对方领袖或对方休息状态角色
        if (targetIsLeader)
        {
            // 领袖必然合法（除非有"无法被攻击"效果，暂不实现）
            return OkResult;
        }
        if (targetId is null) return Fail("未指定目标");
        var target = op.Characters.FirstOrDefault(c => c.Id == targetId.Value);
        if (target is null) return Fail("目标不在对方场上");
        if (!target.IsTapped) return Fail("不能攻击活跃状态的角色");
        return OkResult;
    }

    public static bool HasKeyword(CardInstance c, string kw)
    {
        if (c.Info.EffectText.Contains($"【{kw}】")) return true;
        return c.GainedKeywords.Any(k => k.Keyword == kw);
    }

    public static Result CanDeclareBlocker(GameState s, int playerIdx, Guid blockerId)
    {
        if (s.CurrentBattle is null)             return Fail("没有进行中的战斗");
        if (s.Phase != Phase.BattleBlock)        return Fail("不在阻挡步骤");
        if (s.CurrentBattle.BlockerDeclared)     return Fail("本次战斗已宣言过阻挡者");
        if (s.CurrentBattle.DefenderPlayerIndex != playerIdx) return Fail("你不是防守方");

        var me = s.Players[playerIdx];
        var card = me.Characters.FirstOrDefault(c => c.Id == blockerId);
        if (card is null)              return Fail("阻挡者不在你场上");
        if (card.IsTapped)             return Fail("阻挡者必须为活跃状态");
        if (!HasKeyword(card, "阻挡者")) return Fail("该角色没有【阻挡者】效果");

        // 不可阻挡 keyword 检查
        var atk = s.Players[1 - playerIdx];
        var attackerCard = atk.Characters.FirstOrDefault(c => c.Id == s.CurrentBattle.AttackerCardId)
                           ?? (atk.Leader.Id == s.CurrentBattle.AttackerCardId ? atk.Leader : null);
        if (attackerCard is not null && HasKeyword(attackerCard, "不可阻挡"))
            return Fail("攻击者具有【不可阻挡】");

        return OkResult;
    }
}
