using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-080 月光·莫利亚（领袖）
/// 【启动主要】【每回合1次】可以将我方1张拥有《恐怖之船海盗团》特征的角色KO：
///   本回合中，我方所有领袖和角色力量+1000。
/// 【攻击时】可以丢弃我方的3张手牌：将我方卡组最上方的最多1张卡牌加入生命区最上方。
///
/// 实现说明 / 简化点：
///   - 【启动主要】成本为"KO 我方 1 张《恐怖之船海盗团》角色"，不在 DSL cost 键内，故脚本表达。
///   - 【攻击时】成本为"丢弃 3 张手牌"，选择 3 张弃掉后将卡组顶 1 张加入生命区最上方。
/// </summary>
public class OP14_080_Moria : IScriptedEffect
{
    public string CardNumber => "OP14-080";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.ActivatedMain || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            var key = self.Info.Number + "-act" + ":" + self.Id;
            if (me.TurnOnceUsed.Contains(key)) return;

            var costCands = me.Characters.Where(c => c.Info.HasKeyword("恐怖之船海盗团")).ToList();
            if (costCands.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "月光·莫利亚【启动主要】：KO 我方 1 张《恐怖之船海盗团》角色，本回合我方全体力量+1000？");
            if (!use) return;

            var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "选择 1 张《恐怖之船海盗团》角色作为成本KO",
                costCands.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (costPick.Count < 1) return;
            var costCard = costCands.First(c => c.Id.ToString() == costPick[0]);
            AtomicOps.KO(ctx.State, ctx.OwnerIndex, costCard);

            me.TurnOnceUsed.Add(key);

            // 本回合中，我方所有领袖和角色力量+1000
            AtomicOps.AddPowerToAllThisTurn(ctx.State, ctx.OwnerIndex, c => true, 1000, includeLeader: true);
            return;
        }

        // 【攻击时】可以丢弃 3 张手牌：将卡组顶最多 1 张加入生命区最上方
        if (me.Hand.Count < 3) return;

        bool atk = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "月光·莫利亚【攻击时】：丢弃 3 张手牌，将卡组顶 1 张加入生命区最上方？");
        if (!atk) return;

        var discardPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "选择 3 张手牌丢弃",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 3, 3);
        if (discardPick.Count < 3) return;
        foreach (var id in discardPick)
        {
            var card = me.Hand.First(c => c.Id.ToString() == id);
            AtomicOps.DiscardHand(me, card);
        }

        AtomicOps.AddLifeFromDeckTop(me, 1);
    }
}
