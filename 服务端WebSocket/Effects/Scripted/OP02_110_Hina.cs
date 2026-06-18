using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-110 日奈（角色）
/// 【阻挡者】（关键词，引擎自动处理）
/// 【阻挡时】选择对方最多 1 张费用不高于 6 的角色。本回合中，选中的角色无法攻击。
///
/// 实现说明：
///   - 关键词【阻挡者】由引擎处理，此处仅实现【阻挡时】主动效果。
///   - "无法攻击（本回合）" 用 AddRestriction(CannotAttack, UntilNextOpponentEndPhase)
///     （本卡阻挡发生在对方回合，限制需覆盖对方当前回合的攻击，故用 UntilNextOpponentEndPhase 近似）。
///   - 费用按 CurrentCostOf 评估。
/// </summary>
public class OP02_110_Hina : IScriptedEffect
{
    public string CardNumber => "OP02-110";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnBlockDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 6).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择最多 1 张费用≤6 的对方角色，本回合中无法攻击",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddRestriction(tgt, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);
        }
    }
}
