using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-032 波尔·汉库珂（角色，风）
/// 【不可阻挡】（纯关键词，由引擎处理）
/// 【登场时】直到下个对方的结束阶段结束时为止，对方最多 1 张"蒙奇·D·路飞"以外的角色无法转为休息状态。
///
/// 实现说明：
///   - "无法转为休息状态"用 AtomicOps.AddRestriction(CannotBeRested, UntilNextOpponentEndPhase)，
///     RestCard 会对其 no-op，并由快照下发 cannotBeRested 显示图标（与 OP11-034/OP15-029 同机制）。
///   - 候选排除卡名为"蒙奇·D·路飞"的角色。
/// </summary>
public class OP16_032_BoaHancock : IScriptedEffect
{
    public string CardNumber => "OP16-032";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var candidates = opp.Characters
            .Where(c => !c.MatchesName("蒙奇·D·路飞"))
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterExceptLuffy",
            "选择对方最多 1 张\"蒙奇·D·路飞\"以外的角色（无法转为休息状态，至下个对方结束阶段结束时为止）",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        // 控制者为我方：此限制须持续到"下个对方的结束阶段结束时"，而非我方自己的结束阶段（#146）。
        AtomicOps.AddRestriction(target, RestrictionKind.CannotBeRested, KeywordDuration.UntilNextOpponentEndPhase, ctx.OwnerIndex);
    }
}
