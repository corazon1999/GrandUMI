using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-079
/// 【攻击时】可以将我方卡组最上方的2张卡牌放置到废弃区：本回合中，对方最多1张角色费用-1。
///
/// 可选成本（"可以…：…"）。旧 DSL 占位漏了成本、把费用-1 做成无条件，改脚本：
///   卡组不足2张则不可发动；ConfirmOptional 确认后卡组顶2张废弃(MillTop)，
///   再选对方≤1张角色本回合费用-1(AddCostModifier ThisTurn)。
/// </summary>
public class OP07_079_AttackCostDown : IScriptedEffect
{
    public string CardNumber => "OP07-079";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.Deck.Count < 2) return;          // 卡组顶2张废弃，不足则不可发动
        if (opp.Characters.Count == 0) return;  // 无对方角色可减费

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "【攻击时】将卡组顶2张放入废弃区，使对方最多1张角色本回合费用-1？");
        if (!use) return;

        // 成本：卡组顶2张废弃
        AtomicOps.MillTop(me, 2);

        // 收益：对方最多1张角色本回合费用-1
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选对方最多1张角色，本回合费用-1",
            opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var target = opp.Characters.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddCostModifier(target, -1, KeywordDuration.ThisTurn);
        }
    }
}
