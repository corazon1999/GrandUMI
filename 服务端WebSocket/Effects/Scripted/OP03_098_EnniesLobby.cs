using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-098 艾尼爱斯·司法岛（舞台）
/// 【启动主要】可以将此舞台转为休息状态：我方领袖拥有的特征中包含《CP》的场合，
///   本回合中，对方最多 1 张角色费用-2。
///
/// 说明 / 简化点：
///   - 发动成本为"将此舞台转为休息状态"（用 RestCard）。
///   - 仅当领袖特征含《CP》时才有收益，故仅在该前提下询问是否发动。
///   - 费用-2 用本回合费用修正 AddCostModifier(ThisTurn)。
/// </summary>
public class OP03_098_EnniesLobby : IScriptedEffect
{
    public string CardNumber => "OP03-098";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 领袖特征需含《CP》（CP0/CP9 等以 CP 开头亦计入）
        bool leaderHasCp = me.Leader.Info.Keywords.Any(k => k.Contains("CP"));
        if (!leaderHasCp) return;

        // 已休息则无法支付成本
        if (self.IsTapped) return;

        var targets = opp.Characters;
        if (targets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "艾尼爱斯·司法岛【启动主要】：将此舞台转为休息状态，使对方 1 张角色本回合费用-2？");
        if (!use) return;

        // 成本：将此舞台转为休息状态
        AtomicOps.RestCard(self);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方 1 张角色，本回合费用-2",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddCostModifier(tgt, -2, KeywordDuration.ThisTurn);
        }
    }
}
