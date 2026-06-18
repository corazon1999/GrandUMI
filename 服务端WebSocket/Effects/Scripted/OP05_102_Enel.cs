using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-102 涅槃（角色）
/// 【登场时】将对方最多 1 张费用不高于对方生命卡牌张数的角色 KO。
///
/// 说明：动态阈值"费用 ≤ 对方生命卡牌张数"在 C# 中直接用 opp.LifeCount 作为上限，
/// 候选费用用 c.CurrentCost()（含修正）。"最多 1 张"用 ChooseCards(min=0,max=1)。
/// </summary>
public class OP05_102_Enel : IScriptedEffect
{
    public string CardNumber => "OP05-102";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int threshold = opp.LifeCount;

        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= threshold).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            $"将对方最多 1 张费用≤{threshold} 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
