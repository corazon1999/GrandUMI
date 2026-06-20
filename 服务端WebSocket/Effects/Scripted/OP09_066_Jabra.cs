using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-066 江波尔（角色）
/// 【登场时】对方场上咚!!的张数多于我方场上咚!!的张数的场合，
///   将对方最多 1 张费用不高于 3 的角色 KO。
///
/// 实现说明 / 简化点：
///   - "对方场上咚!!张数多于我方"用费用区咚!!总数比较：opp.TotalDonInCostArea &gt; me.TotalDonInCostArea。
///     （上一轮 DSL 判 complex 仅因 if 条件键只支持与固定阈值比较，C# 脚本可直接做双方相对比较。）
///   - "费用不高于 3 的角色"取卡面原始费用 c.Info.Cost。
/// </summary>
public class OP09_066_Jabra : IScriptedEffect
{
    public string CardNumber => "OP09-066";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：对方场上咚!!的张数多于我方
        if (opp.TotalDonInCostArea <= me.TotalDonInCostArea) return;

        var cands = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张费用≤3 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
