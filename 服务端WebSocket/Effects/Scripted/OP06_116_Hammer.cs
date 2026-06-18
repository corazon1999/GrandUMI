using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-116 排击（事件）
/// 【主要】选择以下的1项。
///   ・将对方最多1张费用不高于5的角色KO。
///   ・对方生命卡牌为1张的场合，给予对方1点伤害。之后，将我方生命区最上方的1张卡牌加入手牌。
///
/// 实现说明 / 简化点：
///   - 用 ChooseOption 二选一。
///   - "给予对方1点伤害"实现为公开对方生命区最上方的1张卡牌，结算其生命触发并加入对方手牌
///     （生命减少1）。本实现简化：直接将对方生命区顶卡移入对方手牌，不结算【触发】。
///   - 之后将我方生命区最上方的1张卡牌加入我方手牌。
/// </summary>
public class OP06_116_Hammer : IScriptedEffect
{
    public string CardNumber => "OP06-116";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        int pick = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "排击：选择以下的1项",
            new List<string>
            {
                "将对方最多1张费用不高于5的角色KO",
                "对方生命为1张时给予1点伤害，之后我方生命顶卡加入手牌",
            });

        if (pick == 0)
        {
            // KO 对方最多1张费用≤5的角色
            var cands = opp.Characters.Where(c => c.CurrentCost() <= 5).ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多1张费用不高于5的角色KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
            return;
        }

        // pick == 1：对方生命为1张时给予1点伤害
        if (opp.LifeArea.Count == 1)
        {
            var dmg = opp.LifeArea[0];
            opp.LifeArea.RemoveAt(0);
            opp.Hand.Add(dmg);

            // 之后：将我方生命区最上方的1张卡牌加入手牌
            if (me.LifeArea.Count > 0)
            {
                var lifeTop = me.LifeArea[0];
                me.LifeArea.RemoveAt(0);
                me.Hand.Add(lifeTop);
            }
        }
    }
}
