using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-060 克洛克达尔（角色 / 暗 / 王下七武海·巴洛克工作室）
/// 【登场时】咚!!-2（可以将我方场上指定数量的咚!!放回咚!!卡组）：
///   我方领袖特征中包含《巴洛克工作室》的场合，将我方卡组最上方最多 1 张卡牌加入生命区最上方。
/// 【对方攻击时】【每回合1次】咚!!-1：抽取 1 张卡牌，丢弃我方 1 张手牌。
///
/// 实现说明：
///   - 登场段：领袖含《巴洛克工作室》且费用区≥2 咚时询问；同意后咚-2，将卡组顶 1 张置入生命顶。
///   - 对方攻击段：每回合 1 次，可选咚-1，抽 1 弃 1（由玩家选择弃哪张）。
/// </summary>
public class OP04_060_Crocodile : IScriptedEffect
{
    public string CardNumber => "OP04-060";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (!me.Leader.Info.HasKeyword("巴洛克工作室")) return;
            if (me.TotalDonInCostArea < 2) return;
            if (me.Deck.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "克洛克达尔【登场时】：咚!!-2，将卡组顶 1 张加入生命区最上方？");
            if (!use) return;

            if (!await AtomicOps.PromptReturnDonToDeck(ctx, 2)) return;
            // 卡组顶 1 张 → 生命区最上方
            AtomicOps.AddLifeFromDeckTop(me, 1);
            return;
        }

        // 【对方攻击时】【每回合1次】咚!!-1：抽 1 弃 1
        var key = self.Info.Number + "-oppatk" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        if (me.TotalDonInCostArea < 1) return;

        bool use2 = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "克洛克达尔【对方攻击时】：咚!!-1，抽取 1 张并丢弃 1 张手牌？");
        if (!use2) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        me.TurnOnceUsed.Add(key);

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

        if (me.Hand.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "丢弃我方 1 张手牌",
                me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (chosen.Count > 0)
            {
                var d = me.Hand.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.DiscardHand(me, d);
            }
        }
    }
}
