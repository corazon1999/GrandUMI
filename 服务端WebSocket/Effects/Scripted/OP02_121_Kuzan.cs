using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-121 库赞（角色）
/// 【我方的回合中】对方所有角色费用-5。
/// 【登场时】将对方最多1张费用为0的角色KO。
///
/// 实现说明：
///   - 持续费用修正用 ContinuousEffect.CostDelta = -5，Scope.Side=1（对方）仅角色，
///     Predicate 限"我方回合中"。来源本卡，离场自动清理；RemoveAll 去重。
///   - 【登场时】用 CurrentCostOf（含持续修正后的当前费用）筛选对方费用为0的角色后询问KO。
///     注意：本卡刚登场即注册了 -5 费用，故此处按"当前费用=0"判定，与文本结果一致
///     （费用-5后变为0的对方角色亦可被选中）。
/// </summary>
public class OP02_121_Kuzan : IScriptedEffect
{
    public string CardNumber => "OP02-121";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;
        int oppIdx = 1 - ctx.OwnerIndex;

        // 持续：我方回合中，对方所有角色费用-5
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = -5,
            Predicate = (s, sideIdx, card) => s.CurrentTurnPlayer == owner,
        });

        // 【登场时】：KO对方最多1张当前费用为0的角色
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(oppIdx, c) <= 0).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多1张费用为0的角色KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, oppIdx, tgt);
        }
    }
}
