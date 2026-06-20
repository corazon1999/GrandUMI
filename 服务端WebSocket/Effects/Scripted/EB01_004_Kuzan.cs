using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-004 空扎（角色 / 炎 / 阿拉巴斯坦王国）
/// 【攻击时】本回合中，可以将我方1张处于活跃状态的领袖力量-5000：本回合中，对方最多1张角色力量-3000。
///
/// 实现说明：
///   - 成本：将我方活跃状态的领袖本回合力量-5000（领袖需处于活跃状态）。
///   - 收益：对方最多1张角色本回合力量-3000。
/// </summary>
public class EB01_004_Kuzan : IScriptedEffect
{
    public string CardNumber => "EB01-004";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本前提：我方领袖处于活跃状态
        if (me.Leader.IsTapped) return;

        var oppChars = opp.Characters.ToList();
        if (oppChars.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "空扎【攻击时】：将我方活跃领袖本回合力量-5000，使对方最多1张角色本回合力量-3000？");
        if (!use) return;

        // 成本：我方活跃领袖本回合力量-5000
        AtomicOps.AddPowerThisTurn(me.Leader, -5000);

        // 收益：对方最多1张角色本回合力量-3000
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "对方最多1张角色本回合力量-3000",
            oppChars.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = oppChars.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, -3000);
        }
    }
}
