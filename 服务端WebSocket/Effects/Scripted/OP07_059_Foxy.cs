using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-059 福克斯（领航）
/// 【攻击时】咚!!-3（可以将我方场上指定数量的咚!!放回咚!!卡组）：
///   我方场上存在 3 张或更多拥有《福克斯海盗团》特征的角色的场合，
///   选择对方最多 1 张处于休息状态的领袖和最多 1 张处于休息状态的角色。
///   被选中的卡牌在下个对方的重置阶段中不会转为活跃状态。
///
/// 实现说明 / 简化点：
///   - 咚!!-3 为可选成本：用 ConfirmOptional 询问，确认后 ReturnDonToDeck(3)。
///     需费用区咚!!总数 ≥3 才可支付。
///   - 条件：我方场上 ≥3 张《福克斯海盗团》特征角色。
///   - “下个对方重置阶段不转活跃”用 PreventActivateNextReset 实现。
///   - 领袖处于休息状态才可选；角色同理。
/// </summary>
public class OP07_059_Foxy : IScriptedEffect
{
    public string CardNumber => "OP07-059";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方场上 ≥3 张《福克斯海盗团》特征角色
        int foxyCount = me.Characters.Count(c => c.Info.HasKeyword("福克斯海盗团"));
        if (foxyCount < 3) return;

        // 可选成本：咚!!-3
        if (me.TotalDonInCostArea < 3) return;
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "福克斯【攻击时】：将 3 张咚!!放回咚!!卡组，使对方休息状态的 1 张领袖和 1 张角色在下个对方重置阶段不转活跃？");
        if (!use) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 3)) return;

        // 选择对方最多 1 张处于休息状态的领袖
        if (opp.Leader.IsTapped)
        {
            var ldChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeader",
                "选择最多 1 张处于休息状态的对方领袖（下个对方重置阶段不转活跃）",
                new List<string> { opp.Leader.Id.ToString() }, 0, 1);
            if (ldChosen.Count > 0)
                AtomicOps.PreventActivateNextReset(opp.Leader);
        }

        // 选择对方最多 1 张处于休息状态的角色
        var restChars = opp.Characters.Where(c => c.IsTapped).ToList();
        if (restChars.Count > 0)
        {
            var chChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张处于休息状态的对方角色（下个对方重置阶段不转活跃）",
                restChars.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chChosen.Count > 0)
            {
                var tgt = restChars.First(c => c.Id.ToString() == chChosen[0]);
                AtomicOps.PreventActivateNextReset(tgt);
            }
        }
    }
}
