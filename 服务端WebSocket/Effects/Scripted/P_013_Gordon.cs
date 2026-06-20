using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-013 哥顿（角色 / 炎 / FILM，cost1 power2000）
/// 【启动主要】可以将此角色放回持有者的卡组最下方：本回合中，对方最多1张角色力量-3000。
///
/// 实现：可选启动主要。成本=将此角色放回卡组最下方（ReturnFieldToDeckBottom）。
///   收益=对方最多 1 张角色本回合 -3000（AddPowerThisTurn）。
///   注意：先选目标，确认发动后再支付成本（避免无目标时白白离场）。
/// </summary>
public class P_013_Gordon : IScriptedEffect
{
    public string CardNumber => "P-013";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "哥顿【启动主要】：将此角色放回卡组最下方，使对方最多 1 张角色本回合力量-3000？");
        if (!use) return;

        // 成本：将此角色放回卡组最下方
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, ctx.Source);

        // 收益：对方最多 1 张角色 -3000
        var cands = opp.Characters.ToList();
        if (cands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合力量-3000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, -3000);
        }
    }
}
