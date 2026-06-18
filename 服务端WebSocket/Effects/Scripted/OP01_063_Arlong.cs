using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-063 阿龙（角色 / 水 / 鱼人族·阿龙海盗团 / 4 费 5000）
/// 【咚!!×1】【启动主要】可以将此角色转为休息状态：选择对方的 1 张手牌公开。
///   公开的卡牌为事件的场合，将对方最多 1 张生命卡牌放置到持有者的卡组最下方。
///
/// 实现说明：
///   - 成本：将此角色转为休息状态（可选发动用 ConfirmOptional）。
///   - 由我方从对方手牌中选 1 张公开（对方手牌为非公开区，传 choiceCards 让客户端显示卡面），
///     用 BroadcastReveal 向双方展示牌面。
///   - 若公开的牌为事件：将对方生命区最上方最多 1 张放置到对方卡组最下方（可选最多 1 张）。
/// </summary>
public class OP01_063_Arlong : IScriptedEffect
{
    public string CardNumber => "OP01-063";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (opp.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿龙【启动主要】：将此角色转为休息状态，选择对方 1 张手牌公开？");
        if (!use) return;

        // 成本：横置自身
        AtomicOps.RestCard(self);

        // 选择对方 1 张手牌公开（对方手牌为非公开区，传卡面）
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = opp.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentHand",
            "选择对方的 1 张手牌公开",
            opp.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (chosen.Count == 0) return;

        var revealed = opp.Hand.First(c => c.Id.ToString() == chosen[0]);
        ctx.Engine?.BroadcastReveal(1 - ctx.OwnerIndex, new List<string> { revealed.Info.Number });

        // 公开的卡为事件：将对方最多 1 张生命牌放置到其卡组最下方
        if (revealed.Info.Kind == CardKind.Event && opp.LifeArea.Count > 0)
        {
            var top = opp.LifeArea[0];
            opp.LifeArea.RemoveAt(0);
            opp.Deck.Add(top);
        }
    }
}
