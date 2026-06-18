using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-113 希路麦波（角色）
/// 【攻击时】本回合中，对方最多 1 张角色费用-2。之后，场上存在费用为 0 的角色的场合，
///   本次战斗中，此角色的力量+2000。
/// 【触发】此卡牌登场。（由触发机制单独处理，此处不实现）
///
/// 实现说明：
///   - 对方最多 1 张角色费用-2（本回合）用 AddCostModifier(-2, ThisTurn)。
///   - "之后场上存在费用为 0 的角色"用 CurrentCostOf 判定（包含刚被-2 后费用变 0 的情形）；
///     成立则本次战斗此角色 +2000（AddPowerThisBattle）。
/// </summary>
public class OP02_113_Hilmeppo : IScriptedEffect
{
    public string CardNumber => "OP02-113";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 对方最多 1 张角色费用-2（本回合）
        if (opp.Characters.Count > 0)
        {
            var ids = opp.Characters.Select(c => c.Id.ToString()).ToList();
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张对方角色，本回合中费用-2", ids, 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = opp.Characters.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddCostModifier(tgt, -2, KeywordDuration.ThisTurn);
            }
        }

        // 之后：场上存在费用为 0 的角色 → 本次战斗 +2000
        bool hasZeroCost =
            me.Characters.Any(c => ctx.State.CurrentCostOf(ctx.OwnerIndex, c) == 0) ||
            opp.Characters.Any(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) == 0);
        if (hasZeroCost)
            AtomicOps.AddPowerThisBattle(ctx.Source, 2000);
    }
}
