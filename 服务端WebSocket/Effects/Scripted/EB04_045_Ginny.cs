using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-045 金妮（角色 / 地 / 革命军）
/// 【启动主要】可以将此角色转为休息状态：场上存在 2 张或更多费用为 8 或更高的角色的场合，
///   本回合中，我方最多 1 张拥有《革命军》特征的领袖或角色力量 +1000。
///
/// 实现说明：
///   - 启动主要、可选（"可以…"）：先 ConfirmOptional，成本为横置自身（RestCard）。
///   - 条件"场上存在 2 张或更多费用为 8 或更高的角色"：统计双方场上角色（卡面原本费用 ≥8），≥2 才有收益。
///   - 收益为本回合力量 +1000（AddPowerThisTurn）。
/// </summary>
public class EB04_045_Ginny : IScriptedEffect
{
    public string CardNumber => "EB04-045";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 自身必须处于活跃状态才能支付"转为休息"成本
        if (self.IsTapped) return;

        // 条件：场上存在 2 张或更多费用为 8 或更高的角色（双方）
        int bigCount =
            me.Characters.Count(c => c.Info.Cost >= 8) +
            opp.Characters.Count(c => c.Info.Cost >= 8);
        if (bigCount < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "金妮【启动主要】：将此角色转为休息状态，使我方最多 1 张《革命军》领袖或角色本回合 +1000？");
        if (!use) return;

        // 成本：横置自身
        AtomicOps.RestCard(self);

        // 收益：我方最多 1 张《革命军》领袖或角色 +1000（本回合）
        var targets = new List<CardInstance>();
        if (me.Leader.Info.HasKeyword("革命军")) targets.Add(me.Leader);
        targets.AddRange(me.Characters.Where(c => c.Info.HasKeyword("革命军")));
        if (targets.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张《革命军》领袖或角色，本回合力量 +1000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, 1000);
        }
    }
}
