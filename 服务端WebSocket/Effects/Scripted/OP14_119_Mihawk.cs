using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-119 杰拉基尔·米霍克（角色 / 王下七武海）
/// 【我方的回合中】当此角色转为休息状态时，直到下个对方的结束阶段结束时为止，
///   对方最多1张费用不高于9的角色无法转为休息状态。
/// 【对方的攻击时】【每回合1次】可以丢弃我方的1张手牌：本次战斗中，我方最多1张领袖或角色力量+2000。
///   （第二段由 DSL OP14.json 处理，本脚本只接第一段）
///
/// 实现说明（反馈#227，此前第一段完全未实现）：
///   - 监听 OnCharRested：引擎已扩展为攻击宣言(reason=attack)/阻挡(reason=block)/效果(reason=effect)
///     均派发；本卡卡面不限来源，任意 reason 都触发（限我方回合+自身）。
///   - "无法转为休息状态"用 AddRestriction(CannotBeRested, UntilNextOpponentEndPhase, 我方)，
///     RestCard 会拦截效果横置；局限：不拦对方用该角色攻击时的自横置（引擎攻击横置不走 RestCard）。
/// </summary>
public class OP14_119_Mihawk : IScriptedEffect
{
    public string CardNumber => "OP14-119";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharRested;

    public async Task Resolve(EffectContext ctx)
    {
        // 仅【我方的回合中】
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;
        // 仅本卡自身被横置（任意来源）
        var restedId = ctx.Vars.TryGetValue("restedCardId", out var v) ? v as string : null;
        if (restedId != ctx.Source.Id.ToString()) return;

        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 9).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "对方最多1张费用不高于9的角色，直到下个对方的结束阶段结束时为止无法转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddRestriction(tgt, RestrictionKind.CannotBeRested,
            KeywordDuration.UntilNextOpponentEndPhase, ctx.OwnerIndex);
    }
}
