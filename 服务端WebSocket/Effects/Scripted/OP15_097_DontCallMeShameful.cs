using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-097 别叫上我，太丢人了!（事件）
/// 【主要】我方废弃区中有 10 张或更多卡牌的场合，直到下个对方的结束阶段结束时为止，
///   对方最多 1 张原本的费用不高于 5 的角色无法攻击。
/// 【触发】发动此卡牌的【主要】效果。
///
/// 实现说明 / 简化点：
///   - "原本的费用不高于 5" 取卡面原始费用 c.Info.Cost。
///   - "无法攻击" 用 AddRestriction(RestrictionKind.CannotAttack, ...)；
///     "直到下个对方的结束阶段结束时" 用 KeywordDuration.UntilNextOpponentEndPhase。
///   - 仅当我方废弃区 ≥10 张时本效果有收益；否则无收益直接返回。
///   - 【触发】与【主要】结算相同，故 EventMain / OnLifeRevealTrigger 共用同一逻辑。
/// </summary>
public class OP15_097_DontCallMeShameful : IScriptedEffect
{
    public string CardNumber => "OP15-097";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方废弃区中有 10 张或更多卡牌
        if (me.Trash.Count < 10) return;

        // 对方最多 1 张原本的费用不高于 5 的角色
        var cands = opp.Characters.Where(c => c.Info.Cost <= 5).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张原本费用不高于 5 的角色，使其无法攻击（直到下个对方结束阶段）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = cands.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null)
            AtomicOps.AddRestriction(target, RestrictionKind.CannotAttack,
                KeywordDuration.UntilNextOpponentEndPhase);
    }
}
