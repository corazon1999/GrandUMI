using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-078 百万吨九尾快攻（事件）
/// 【主要】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，将我方最多 1 张"福克斯"转为活跃状态。
///
/// 实现说明 / 简化点：
///   - 条件"我方咚数 ≤ 对方咚数"用费用区咚!!总数动态比较：me.TotalDonInCostArea &lt;= opp.TotalDonInCostArea。
///   - "福克斯"用 MatchesName("福克斯") 匹配；仅休息状态的角色才有"转为活跃"的意义，故候选取已横置者。
///   - "最多 1 张"由玩家选择 0~1 张目标。
///   - 卡面【触发】(从咚!!卡组追加最多 1 张活跃咚!!)由触发节单独处理，不在本脚本内。
/// </summary>
public class OP07_078_MillionTonKyubiKaisoku : IScriptedEffect
{
    public string CardNumber => "OP07-078";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方场上咚数 ≤ 对方场上咚数
        if (me.TotalDonInCostArea > opp.TotalDonInCostArea) return;

        // 将我方最多 1 张"福克斯"（休息状态）转为活跃
        var cands = me.Characters.Where(c => c.IsTapped && c.MatchesName("福克斯")).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多 1 张\"福克斯\"转为活跃状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var target = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.ActivateCard(target);
        }
    }
}
