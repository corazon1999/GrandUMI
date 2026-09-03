using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Effects.Rules;

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

    public static Result CanChooseFirstPlayer(GameState s, int playerIdx, bool _)
    {
        if (s.StartingPlayerChosen) return Fail("先后手已经确定");
        if (s.StartingPlayerChooser != playerIdx) return Fail("本次骰点胜者才可选择先后手");
        return OkResult;
    }

    public static Result CanMulligan(GameState s, int playerIdx, bool redraw)
    {
        if (!s.StartingPlayerChosen) return Fail("请先完成先后手选择");
        var player = s.Players[playerIdx];
        if (player.MulliganDone) return Fail("已完成换牌");
        if (redraw && !player.HasReDraw) return Fail("已经没有重抽机会");
        return OkResult;
    }

    public static Result CanRespondPrompt(
        GameState s,
        int playerIdx,
        string promptId,
        IReadOnlyList<string> chosen)
    {
        var pending = s.PendingPrompt;
        if (pending is null) return Fail("没有待响应的 prompt");
        if (pending.PlayerIndex != playerIdx) return Fail("不是你的 prompt");
        if (!string.Equals(promptId, pending.PromptId, StringComparison.Ordinal)) return Fail("promptId 不匹配");
        if (chosen.Count < pending.MinChoose || chosen.Count > pending.MaxChoose) return Fail("选择数量不符合要求");
        if (chosen.Distinct(StringComparer.Ordinal).Count() != chosen.Count) return Fail("不能重复选择同一项");
        var valid = pending.ValidChoices.ToHashSet(StringComparer.Ordinal);
        if (chosen.Any(id => !valid.Contains(id))) return Fail("包含不可选择的项目");
        return OkResult;
    }

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
        if (card.Kind == CardKind.Event
            && Array.IndexOf(card.EffectTags, "EventMain") < 0)
            return Fail("该事件没有【主要】效果，不能在主要阶段发动");
        if (card.Kind == CardKind.Event
            && !Hex.HexRules.CanActivateHandEventEffect(s, playerIdx))
            return Fail("【神射法师】使手牌事件不能发动效果，只能作为+4000反击值使用");
        if (card.Kind == CardKind.Character && s.NoPlayCharacterThisTurn.Contains(playerIdx))
            return Fail("本回合无法登场角色卡牌");
        if (card.Kind == CardKind.Character
            && s.NoPlayCharacterOriginalCostGteThisTurn.TryGetValue(playerIdx, out int blockedCost)
            && card.Cost >= blockedCost)
            return Fail($"本回合无法登场原本费用不低于{blockedCost}的角色卡牌");
        int effCost = s.HandPlayCost(playerIdx, p.Hand[handIndex]);
        if (p.ActiveDonCount < effCost)       return Fail($"费用不足，需要 {effCost}");
        if (card.Kind == CardKind.Event
            && CardRulesetManager.For(s).TryGetScriptedEffect(card.Number) is IEventMainAvailability availability
            && availability.GetEventMainUnavailableReason(s, playerIdx, p.Hand[handIndex], effCost) is { } reason)
            return Fail(reason);
        // 角色区满员（≥5）不再拒绝：登场时由玩家选择 1 张己方角色废弃后再登场
        // （见 GameEngine.PlayCharacterWithOverflowAsync）
        return OkResult;
    }

    public static Result CanAttachDon(GameState s, int playerIdx, string targetId, int count = 1)
    {
        if (s.CurrentTurnPlayer != playerIdx) return Fail("不是你的回合");
        if (s.Phase != Phase.Main)            return Fail("只能在主要阶段赋予咚");
        if (s.CurrentBattle is not null)      return Fail("战斗中不能赋予咚");
        if (Hex.HexRules.Has(s, playerIdx, 12)) return Fail("【回归基本功】使你无法手动赋予咚!!");
        if (count < 1)                        return Fail("赋予咚数量必须是正整数");
        var p = s.Players[playerIdx];
        if (p.ActiveDonCount < count)         return Fail($"活跃咚不足，需要 {count} 张");
        if (targetId == "leader") return OkResult;
        if (!Guid.TryParse(targetId, out var gid)) return Fail("目标非法");
        if (!p.Characters.Any(c => c.Id == gid))    return Fail("目标不在场上");
        return OkResult;
    }

    public static Result CanUndoAttachDon(GameState s, int playerIdx, long operationSequence)
    {
        if (s.CurrentTurnPlayer != playerIdx) return Fail("不是你的回合");
        if (s.Phase != Phase.Main)            return Fail("只能在主要阶段撤回贴咚");
        if (s.CurrentBattle is not null)      return Fail("战斗中不能撤回贴咚");
        var latest = s.AttachDonUndoStack.LastOrDefault();
        if (latest is null || latest.PlayerIndex != playerIdx)
            return Fail("当前没有可撤回的贴咚");
        if (latest.OperationSequence != operationSequence)
            return Fail("撤回目标已过期，请以最新对局状态为准");
        return OkResult;
    }

    public static Result CanDetachAllDon(GameState s, int playerIdx, Guid characterId)
    {
        if (s.CurrentTurnPlayer != playerIdx) return Fail("不是你的回合");
        if (s.Phase != Phase.Main) return Fail("只能在主要阶段移除咚");
        if (s.CurrentBattle is not null) return Fail("战斗中不能移除咚");
        if (!Hex.HexRules.Has(s, playerIdx, 70)) return Fail("未持有【屠宰场】");
        var player = s.Players[playerIdx];
        if (!player.Characters.Any(card => card.Id == characterId)) return Fail("目标不是己方场上角色");
        if (player.AttachedDonCount(characterId) <= 0) return Fail("目标角色没有附加咚");
        return OkResult;
    }

    public static Result CanAttack(GameState s, int playerIdx, Guid attackerId, bool targetIsLeader, Guid? targetId)
    {
        if (s.CurrentTurnPlayer != playerIdx) return Fail("不是你的回合");
        if (s.Phase != Phase.Main)            return Fail("只能在主要阶段宣言攻击");
        if (s.CurrentBattle is not null)      return Fail("已有战斗进行中");
        if (!Hex.HexRules.CanDeclareAnotherAttack(s, playerIdx))
            return Fail("【一板一眼】使你本回合只能宣言1次攻击");
        // TurnCount 记录的是整场对局中经过的玩家回合数：
        // 1 为先手的首回合，2 为后手的首回合。双方各自的首回合均不能攻击。
        if (s.TurnCount <= 2)                 return Fail("双方各自第 1 回合不能战斗");

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
        if (!AtomicOps.CanRestCard(s, attacker, playerIdx))
            return Fail("【霸王色霸气】使当前力量5000或以下的角色无法转为休息，不能攻击");
        if (HasCannotAttackStatus(s, attacker)
            && !Hex.HexRules.LeaderIgnoresCannotAttack(s, playerIdx, attacker))
            return Fail("此角色当前无法攻击");

        // 新登场角色当回合不能攻击（除非有【速攻】；或"登场回合可攻击角色"且本次目标为角色——OP04-096）
        if (attacker != me.Leader && attacker.TurnPlayed == s.TurnCount
            && !HasKeyword(s, attacker, "速攻")
            && !(HasKeyword(s, attacker, "登场回合可攻击角色") && !targetIsLeader))
            return Fail("新登场角色无法攻击");

        // 「登场回合无法攻击领袖」：即使同时获得【速攻】，本回合也只能攻击角色（OP03-004）。
        if (targetIsLeader && attacker != me.Leader && attacker.TurnPlayed == s.TurnCount
            && HasKeyword(s, attacker, "登场回合无法攻击领袖"))
            return Fail("此角色在登场回合中无法攻击领袖");

        // OP17-044 约翰船长：满足条件且处于休息状态时，对方只能攻击同名角色（领袖也不能成为目标）。
        bool johnTargetLock = op.Characters.Any(c =>
            c.Info.Number == "OP17-044"
            && c.IsTapped
            && !c.IsEffectsNullified
            && op.Leader.Info.HasKeyword("洛克斯海盗团"));

        // 目标：对方领袖或对方休息状态角色
        if (targetIsLeader)
        {
            if (johnTargetLock) return Fail("对方效果限制：只能攻击“约翰船长”角色");
            if (Hex.HexRules.FirstAttackMustTargetRestedCharacter(s, playerIdx))
                return Fail("【线线果实】使本回合第一次攻击必须以休息状态角色为目标");
            if (ReferenceEquals(attacker, me.Leader)
                && !Hex.HexRules.OpposingLeaderMayAttackLeader(s, playerIdx))
                return Fail("【给我一个面子】使领袖不能攻击该领袖");
            if (ReferenceEquals(attacker, me.Leader) && !Hex.HexRules.LeaderMayAttackLeader(s, playerIdx))
                return Fail("【亮出你的剑】使领袖不能攻击敌方领袖");
            if (!Hex.HexRules.MayAttackProtectedZeroLifeLeader(s, 1 - playerIdx))
                return Fail("【我是天龙人】保护了生命为0且仍有休息角色的领袖");
            // 领袖必然合法（除非有"无法被攻击"效果，暂不实现）
            return OkResult;
        }
        if (targetId is null) return Fail("未指定目标");
        var target = op.Characters.FirstOrDefault(c => c.Id == targetId.Value);
        if (target is null) return Fail("目标不在对方场上");
        if (Hex.HexRules.FirstAttackMustTargetRestedCharacter(s, playerIdx) && !target.IsTapped)
            return Fail("【线线果实】使本回合第一次攻击必须以休息状态角色为目标");
        // 防守方存在生效中的攻击目标锁定时，只能攻击对应名称的角色；攻击领袖不受影响（OP01-051）。
        bool kidTargetLock = op.Characters.Any(c =>
            HasKeyword(s, c, "仅可攻击角色：尤斯塔斯·基德"));
        if (kidTargetLock && !target.MatchesName("尤斯塔斯·基德"))
            return Fail("对方效果限制：只能攻击“尤斯塔斯·基德”角色");
        if (johnTargetLock && !target.MatchesName("约翰船长"))
            return Fail("对方效果限制：只能攻击“约翰船长”角色");
        // 通常只能攻击休息状态角色；带"可攻击活跃"授予的攻击者可攻击活跃角色
        if (!target.IsTapped && !HasKeyword(s, attacker, "可攻击活跃")) return Fail("不能攻击活跃状态的角色");
        // 按费用禁攻（OP12-020：本回合此领袖无法攻击对方原本费用≤N角色）
        if (attacker.NoAttackCostLeThisTurn > 0 && target.Info.Cost <= attacker.NoAttackCostLeThisTurn)
            return Fail($"本回合无法攻击原本费用≤{attacker.NoAttackCostLeThisTurn}的角色");
        return OkResult;
    }

    public static bool HasKeyword(GameState s, CardInstance c, string kw)
    {
        if (kw == "不可阻挡")
        {
            int side = s.SideOf(c);
            if (side is 0 or 1 && ReferenceEquals(s.Players[side].Leader, c) && Hex.HexRules.Has(s, side, 7))
                return true;
        }
        // “效果无效”会移除该角色自身印刷的永续关键词（如【速攻】），
        // 但不会抹掉由其他仍生效卡牌赋予它的关键词。
        if (c.IsEffectsNullified || s.IsContinuouslyNullified(c))
            return HasGrantedKeyword(s, c, kw);
        // 官网使用【速攻：角色】表示仅在登场回合可攻击角色；引擎内部沿用语义化名称。
        if (kw == "登场回合可攻击角色" && Array.IndexOf(c.Info.Abilities, "速攻：角色") >= 0) return true;
        if (Array.IndexOf(c.Info.Abilities, kw) >= 0) return true;
        return HasGrantedKeyword(s, c, kw);
    }

    private static bool HasGrantedKeyword(GameState s, CardInstance c, string kw)
    {
        int side = s.SideOf(c);
        if ((side is 0 or 1 && Hex.HexRules.GrantsKeyword(s, side, c, kw))
            || c.GainedKeywords.Any(k => k.Keyword == kw)
            || s.HasContinuousKeyword(c, kw))
            return true;

        // 正式词条与历史内部语义名互认，兼容既有脚本和新卡牌数据。
        string? alias = kw switch
        {
            "登场回合可攻击角色" => "速攻：角色",
            "速攻：角色" => "登场回合可攻击角色",
            _ => null,
        };
        return alias is not null
            && ((side is 0 or 1 && Hex.HexRules.GrantsKeyword(s, side, c, alias))
                || c.GainedKeywords.Any(k => k.Keyword == alias)
                || s.HasContinuousKeyword(c, alias));
    }

    public static Result CanUseEffect(GameState s, int playerIdx, Guid sourceId)
    {
        if (s.CurrentTurnPlayer != playerIdx) return Fail("不是你的回合");
        if (s.Phase != Phase.Main)            return Fail("只能在主要阶段使用启动效果");
        if (s.CurrentBattle is not null)      return Fail("战斗中不能使用启动效果");
        var me = s.Players[playerIdx];
        var source = me.Leader.Id == sourceId ? me.Leader
            : me.Characters.FirstOrDefault(c => c.Id == sourceId)
              ?? (me.StageCard?.Id == sourceId ? me.StageCard : null)
              ?? (me.ExtraStageCard?.Id == sourceId ? me.ExtraStageCard : null);
        if (source is null) return Fail("效果来源不在你场上");
        if (Array.IndexOf(source.Info.EffectTags, "ActivatedMain") < 0)
            return Fail("该卡没有【启动主要】效果");

        // 整卡效果无效时，效果根本不能发动；不能先接受请求、广播发动，再在运行时静默跳过。
        if (source.IsEffectsNullified || s.IsContinuouslyNullified(source))
            return Fail("该卡效果当前无效，不能发动启动效果");

        if (!HasMetCardSpecificActivationTiming(s, playerIdx, source))
            return Fail("该效果要到你的第2回合及以后才能发动");

        var scripted = CardRulesetManager.For(s).TryGetScriptedEffect(source.Info.Number);
        if (scripted is IActivatedMainAvailability availability
            && availability.GetActivatedMainUnavailableReason(s, playerIdx, source) is { } reason)
            return Fail(reason);

        // OP17-044 的「将此角色转为休息状态」是发动成本；无法完成该状态变更时不能发动。
        if (source.Info.Number == "OP17-044"
            && (source.IsTapped
                || source.HasRestriction(RestrictionKind.CannotBeRested)
                || s.HasContinuousRestriction(source, RestrictionKind.CannotBeRested)))
            return Fail("约翰船长当前无法转为休息状态，不能支付发动成本");
        return OkResult;
    }

    /// <summary>
    /// 是否存在明确的“无法攻击”状态。只统计卡牌限制、持续限制和卡牌自带禁攻，
    /// 不把非当前回合、已休息、新登场等普通攻击条件误判为禁攻状态。
    /// </summary>
    public static bool HasCannotAttackStatus(GameState s, CardInstance card)
    {
        int side = s.SideOf(card);
        if (side is 0 or 1 && Hex.HexRules.LeaderIgnoresCannotAttack(s, side, card))
            return false;
        return card.HasRestriction(RestrictionKind.CannotAttack)
           || (!(card.IsEffectsNullified || s.IsContinuouslyNullified(card))
               && (s.HasContinuousRestriction(card, RestrictionKind.CannotAttack)
                   || Array.IndexOf(card.Info.Abilities, "此角色无法攻击") >= 0));
    }

    public static Result CanPassBlock(GameState s, int playerIdx)
    {
        if (s.CurrentBattle is null) return Fail("无战斗");
        if (s.Phase != Phase.BattleBlock) return Fail("不在阻挡步骤");
        if (s.CurrentBattle.DefenderPlayerIndex != playerIdx) return Fail("不是防守方");
        return OkResult;
    }

    public static Result CanPlayCounter(GameState s, int playerIdx, int handIndex, bool useCounterIcon)
    {
        if (s.CurrentBattle is null || s.Phase != Phase.BattleCounter) return Fail("不在反击步骤");
        if (s.CurrentBattle.DefenderPlayerIndex != playerIdx) return Fail("不是防守方");
        var defender = s.Players[playerIdx];
        if (handIndex < 0 || handIndex >= defender.Hand.Count) return Fail("手牌索引非法");
        var card = defender.Hand[handIndex];
        if (useCounterIcon)
            return HandStaticCounter.Value(s, playerIdx, card) > 0
                ? OkResult
                : Fail("该卡无反击值");
        if (card.Info.Kind == CardKind.Event
            && !Hex.HexRules.CanActivateHandEventEffect(s, playerIdx))
            return Fail("【神射法师】使手牌事件不能发动效果，只能作为+4000反击值使用");
        if (Array.IndexOf(card.Info.EffectTags, "EventCounter") < 0) return Fail("该卡没有反击效果");
        int cost = s.HandPlayCost(playerIdx, card);
        if (defender.ActiveDonCount < cost) return Fail("活跃咚不足，无法打出该反击事件");
        return OkResult;
    }

    public static Result CanPassCounter(GameState s, int playerIdx)
    {
        if (s.CurrentBattle is null || s.Phase != Phase.BattleCounter) return Fail("不在反击步骤");
        if (s.CurrentBattle.DefenderPlayerIndex != playerIdx) return Fail("不是防守方");
        return OkResult;
    }

    /// <summary>卡牌专属的启动主要时点限制；同时供动作校验与快照按钮可用性复用。</summary>
    public static bool HasMetCardSpecificActivationTiming(GameState s, int playerIdx, CardInstance source)
    {
        if (source.Info.Number != "OP15-058") return true;
        int secondOwnTurn = s.FirstPlayer == playerIdx ? 3 : 4;
        return s.TurnCount >= secondOwnTurn;
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
        if (!HasKeyword(s, card, "阻挡者")) return Fail("该角色没有【阻挡者】效果");
        if (!AtomicOps.CanRestCard(s, card, playerIdx))
            return Fail("【霸王色霸气】使当前力量5000或以下的角色无法转为休息，不能发动【阻挡者】");
        if (card.HasRestriction(RestrictionKind.CannotBeBlocker)
            || s.HasContinuousRestriction(card, RestrictionKind.CannotBeBlocker))
            return Fail("该角色本回合不能发动【阻挡者】");

        // 不可阻挡 keyword 检查
        var atk = s.Players[1 - playerIdx];
        var attackerCard = atk.Characters.FirstOrDefault(c => c.Id == s.CurrentBattle.AttackerCardId)
                           ?? (atk.Leader.Id == s.CurrentBattle.AttackerCardId ? atk.Leader : null);
        if (attackerCard is not null && HasKeyword(s, attackerCard, "不可阻挡"))
            return Fail("攻击者具有【不可阻挡】");

        return OkResult;
    }
}
