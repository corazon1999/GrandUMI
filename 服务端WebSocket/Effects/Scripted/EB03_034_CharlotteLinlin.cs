using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-034 夏洛特·玲玲（角色 / 暗 / 洛克斯海盗团，7 费 7000）
/// 【登场时】抽取 1 张卡牌，并将我方的 1 张手牌放回卡组最上方。
///   之后，从咚!!卡组中追加最多 1 张活跃状态的咚!!。
/// 【KO时】咚!!-1：将我方卡组最上方的最多 1 张卡牌加入生命区最上方。
///
/// 实现说明：
///   - 登场段：Draw(1)，再从手牌选 1 张放回卡组最上方(手动 Remove + Deck.Insert(0,..))；
///     之后 RefreshDonFromDeck(me, 1, Active) 追加 1 张活跃咚。
///   - KO段：成本"咚!!-1"(ReturnDonToDeck，需费用区 ≥1)，先 ConfirmOptional；
///     之后 AddLifeFromDeckTop(me, 1) 将卡组顶 1 张加入生命区最上方。
/// </summary>
public class EB03_034_CharlotteLinlin : IScriptedEffect
{
    public string CardNumber => "EB03-034";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 抽 1 张
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

            // 将我方 1 张手牌放回卡组最上方
            if (me.Hand.Count > 0)
            {
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                    "将 1 张手牌放回卡组最上方",
                    me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
                if (chosen.Count > 0)
                {
                    var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
                    if (card is not null)
                    {
                        me.Hand.Remove(card);
                        me.Deck.Insert(0, card);
                    }
                }
            }

            // 之后：从咚!!卡组追加最多 1 张活跃状态的咚!!
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
            return;
        }

        // 【KO时】咚!!-1：将卡组顶最多 1 张加入生命区最上方
        if (me.TotalDonInCostArea < 1) return; // 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "夏洛特·玲玲【KO时】：咚!!-1，将卡组顶最多 1 张加入生命区最上方?");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        AtomicOps.AddLifeFromDeckTop(me, 1);
    }
}
