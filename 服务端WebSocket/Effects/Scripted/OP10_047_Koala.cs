using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-047 克尔拉（角色）
/// 【攻击时】可以将我方 1 张费用为 3 或更高且拥有《革命军》特征的角色放回其持有者的手牌：
///   本回合中，此角色的力量 +3000。
///
/// 实现说明：
/// - 成本（退回我方费≥3 且《革命军》角色）与收益（+3000）强耦合：仅当支付成本时才获得 +3000。
/// - C# 在【攻击时】用 ConfirmOptional 询问，支付成本（BounceToHand 我方目标）后再加力量。
/// </summary>
public class OP10_047_Koala : IScriptedEffect
{
    public string CardNumber => "OP10-047";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 可作为成本退回的候选：我方费用≥3 且拥有《革命军》特征的角色
        var cands = me.Characters
            .Where(c => c.Info.Cost >= 3 && c.Info.HasKeyword("革命军"))
            .ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "克尔拉【攻击时】：将我方 1 张费用≥3 且《革命军》角色放回手牌，使此角色本回合 +3000？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张费用≥3 且《革命军》角色放回手牌（成本）",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return; // 未支付成本 → 不获得收益

        var costCard = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, costCard);

        // 收益：此角色本回合 +3000
        AtomicOps.AddPowerThisTurn(self, 3000);
    }
}
