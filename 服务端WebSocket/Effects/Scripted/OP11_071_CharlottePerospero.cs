using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-071 夏洛特·佩洛斯佩罗（角色 5 费 7000，大妈海盗团）
/// 【启动主要】【每回合1次】可以丢弃我方的 1 张手牌：宣言任意的费用，并公开对方卡组最上方的 1 张卡牌。
///   公开的卡牌费用与宣言的费用相同的场合，抽取 1 张卡牌，并从咚!!卡组中追加最多 1 张活跃状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 成本：丢弃我方 1 张手牌（须有手牌）。每回合 1 次（TurnOnceUsed）。
///   - "宣言任意费用"用 ChooseOption（0..10）；公开 opp.Deck[0]，比较其卡面原费用。
///   - 命中：抽 1 张，并从咚卡组追加 1 张活跃咚。
/// </summary>
public class OP11_071_CharlottePerospero : IScriptedEffect
{
    public string CardNumber => "OP11-071";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "佩洛斯佩罗【启动主要】：丢弃 1 张手牌，宣言费用并公开对方卡组顶 1 张，命中则抽 1 张并追加 1 张活跃咚？");
        if (!use) return;

        // 成本：丢弃我方 1 张手牌（由玩家选择）
        var discExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃我方 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discExtra);
        if (disc.Count == 0) return; // 成本未支付
        var dCard = me.Hand.First(c => c.Id.ToString() == disc[0]);
        AtomicOps.DiscardHand(me, dCard);

        me.TurnOnceUsed.Add(key);

        // 宣言任意费用
        var costOptions = Enumerable.Range(0, 11).Select(i => i.ToString()).ToList();
        int declared = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "宣言任意的费用", costOptions);

        // 公开对方卡组最上方 1 张
        if (opp.Deck.Count == 0) return;
        var revealed = opp.Deck[0];
        if (revealed.Info.Cost != declared) return; // 未命中

        // 命中：抽 1 张 + 追加 1 张活跃咚
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
    }
}
