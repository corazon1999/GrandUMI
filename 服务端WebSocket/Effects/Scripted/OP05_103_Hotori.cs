using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-103 小鸟（角色）
/// 【登场时】我方场上存在"小边"的场合，将对方最多 1 张费用不高于对方生命卡牌张数的角色 KO。
///
/// 说明：条件"我方场上存在小边"用 me.Characters.Any(c => c.MatchesName("小边"))；
/// 动态阈值"费用 ≤ 对方生命卡牌张数"直接用 opp.LifeCount 作为上限，候选费用用 c.CurrentCost()。
/// </summary>
public class OP05_103_Hotori : IScriptedEffect
{
    public string CardNumber => "OP05-103";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方场上存在"小边"
        if (!me.Characters.Any(c => c.MatchesName("小边"))) return;

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
