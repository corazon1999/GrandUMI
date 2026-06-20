using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-038 特拉法尔加·罗（角色）
/// 【登场时】可以将我方的 1 张领袖转为休息状态：将对方最多 1 张费用不高于 1 的角色 KO。
///
/// 实现说明：
/// - "横置我方 1 张领袖" 为可选成本（DSL 触发节不支持横置领袖成本），用脚本实现。
/// - 仅在领袖处于活跃且对方存在费用≤1角色时才有意义；ConfirmOptional 询问是否发动。
/// </summary>
public class P_038_Law : IScriptedEffect
{
    public string CardNumber => "P-038";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 领袖需为活跃状态才能横置作为成本
        if (me.Leader.IsTapped) return;

        var cands = opp.Characters.Where(c => c.Info.Cost <= 1).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗【登场时】：将我方 1 张领袖转为休息状态，KO 对方最多 1 张费用≤1 的角色？");
        if (!use) return;

        // 成本：横置我方领袖
        AtomicOps.RestCard(me.Leader);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择 1 张费用≤1 的对方角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
