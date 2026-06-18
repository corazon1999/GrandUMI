using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-093 马歇尔·D·提奇（角色 / 地 / 黑胡子海盗团）
/// 【阻挡者】（关键词由引擎处理）
/// 【启动主要】【每回合1次】我方领袖拥有《黑胡子海盗团》特征，且在此角色登场的回合的场合：
///   1) 本回合中，对方最多 1 张领袖的效果无效。
///   2) 之后，直到下个对方的回合结束时为止，对方最多 1 张角色的效果无效，且该角色无法攻击。
///
/// 实现说明 / 简化点：
///   - 条件"领袖拥有《黑胡子海盗团》特征"用 Leader.Info.HasKeyword 判定；
///     "在此角色登场的回合"用 self.TurnPlayed == 当前 TurnCount 判定。
///   - 领袖效果无效：本回合用 AtomicOps.NullifyEffects(ThisTurn)。本作领袖通常唯一，直接对其生效。
///   - 角色"直到下个对方回合结束效果无效"用 ContinuousEffect.NullifyEffect 注册，
///     Predicate 以注册时回合数 baseTurn 限定有效期(s.TurnCount <= baseTurn + 1)，到期自动失效；
///     "无法攻击"用 AddRestriction(CannotAttack, UntilNextOpponentEndPhase)。
/// </summary>
public class OP09_093_Teach : IScriptedEffect
{
    public string CardNumber => "OP09-093";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        int oppIdx = 1 - ctx.OwnerIndex;

        // 每回合 1 次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 条件：领袖拥有《黑胡子海盗团》特征，且此角色为本回合登场
        if (!me.Leader.Info.HasKeyword("黑胡子海盗团")) return;
        if (self.TurnPlayed != ctx.State.TurnCount) return;

        me.TurnOnceUsed.Add(key);

        // 1) 本回合中，对方领袖效果无效
        if (opp.Leader != null) AtomicOps.NullifyEffects(opp.Leader, KeywordDuration.ThisTurn);

        // 2) 之后：对方最多 1 张角色，直到下个对方回合结束效果无效且无法攻击
        if (opp.Characters.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，直到下个对方回合结束效果无效且无法攻击",
            opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = opp.Characters.First(c => c.Id.ToString() == chosen[0]);

        // 无法攻击（直到下个对方结束阶段）
        AtomicOps.AddRestriction(target, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);

        // 效果无效（持续，直到下个对方回合结束）
        var tgtId = target.Id;
        int baseTurn = ctx.State.TurnCount;
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = self.Id.ToString(),
            Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
            NullifyEffect = true,
            Predicate = (s, sideIdx, card) => card.Id == tgtId && s.TurnCount <= baseTurn + 1,
        });
    }
}
