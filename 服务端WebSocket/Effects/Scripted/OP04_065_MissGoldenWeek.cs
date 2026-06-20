using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-065 Ms.黄金周(玛丽安奴)（角色 / 暗 / 巴洛克工作室）
/// 【登场时】我方领袖特征中包含《巴洛克工作室》的场合，直到下个我方回合开始时为止，
///   对方最多 1 张费用不高于 5 的角色无法攻击。
///
/// 实现说明：
///   - 领袖含《巴洛克工作室》时，从对方费用≤5 的角色中选最多 1 张，
///     施加 CannotAttack 限制（KeywordDuration.UntilNextOpponentEndPhase，即覆盖对方下个回合）。
///   - ActionValidator 已对 HasRestriction(CannotAttack) 生效，故对对方角色禁攻有效。
/// </summary>
public class OP04_065_MissGoldenWeek : IScriptedEffect
{
    public string CardNumber => "OP04-065";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (!me.Leader.Info.HasKeyword("巴洛克工作室")) return;

        var cands = opp.Characters.Where(c => c.Info.Cost <= 5).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张费用≤5 的角色，使其无法攻击（直到下个我方回合开始）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddRestriction(tgt, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);
        }
    }
}
