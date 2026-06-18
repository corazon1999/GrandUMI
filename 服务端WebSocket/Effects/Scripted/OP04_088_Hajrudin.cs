using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-088 海尔丁（角色 / 地 / 巨人族·德莱斯罗兹·新巨兵海盗团）
/// 【启动主要】可以将我方的 1 张领袖转为休息状态：本回合中，对方最多 1 张角色费用-4。
///
/// 实现说明：
///   - 成本 = 横置我方领袖（RestCard）。领袖须为活跃才可发动。
///   - 收益 = 对方最多 1 张角色 AddCostModifier(-4, ThisTurn)。
/// </summary>
public class OP04_088_Hajrudin : IScriptedEffect
{
    public string CardNumber => "OP04-088";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.Leader.IsTapped) return; // 领袖须为活跃才可横置

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "海尔丁【启动主要】：将我方领袖转为休息状态，本回合对方最多 1 张角色费用-4？");
        if (!use) return;

        AtomicOps.RestCard(me.Leader);

        var cands = opp.Characters.ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合费用-4",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddCostModifier(tgt, -4, KeywordDuration.ThisTurn);
        }
    }
}
