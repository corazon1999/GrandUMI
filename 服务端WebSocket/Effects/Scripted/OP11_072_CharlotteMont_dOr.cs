using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-072 夏洛特·蒙道尔（角色 3 费 4000，大妈海盗团）
/// 【启动主要】【每回合1次】咚!!-1，可以将此角色转为休息状态：
///   对方将其废弃区中的 2 张卡牌自选顺序放回其卡组最下方。
///   之后，将我方生命区最上方的 1 张卡牌加入手牌。
///
/// 实现说明 / 简化点：
///   - 成本：咚!!-1 + 将此角色转为休息状态（须为活跃且有 ≥1 张活跃咚）。
///   - "对方将其废弃区 2 张放回卡组最下方"：由对方玩家选择 2 张（不足 2 张则选全部），
///     按所选顺序放回对方卡组最下方。
///   - "我方生命区最上方 1 张加入手牌"：取 LifeArea[0] 入手。
/// </summary>
public class OP11_072_CharlotteMont_dOr : IScriptedEffect
{
    public string CardNumber => "OP11-072";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        int oppIdx = 1 - ctx.OwnerIndex;

        // 每回合1次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本前置条件：自身活跃 + 有可放回的咚
        if (self.IsTapped) return;
        if (me.CostArea.Count < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "蒙道尔：支付咚!!-1并将此角色转为休息，让对方将其废弃区 2 张放回卡组底，之后我方生命区顶 1 张入手？");
        if (!use) return;

        // 支付成本
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        AtomicOps.RestCard(self);
        me.TurnOnceUsed.Add(key);

        // 对方将其废弃区 2 张放回卡组最下方（由对方选择）
        var trash = opp.Trash.ToList();
        if (trash.Count > 0)
        {
            int n = Math.Min(2, trash.Count);
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = trash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var picks = await ctx.Prompts.ChooseCards(oppIdx, "OwnTrash",
                $"将你废弃区的 {n} 张卡牌放回你的卡组最下方",
                trash.Select(c => c.Id.ToString()).ToList(), n, n, extra);
            foreach (var id in picks)
            {
                var card = trash.FirstOrDefault(c => c.Id.ToString() == id);
                if (card is not null) AtomicOps.ReturnTrashToDeckBottom(opp, card);
            }
        }

        // 之后：我方生命区最上方 1 张加入手牌
        if (me.LifeArea.Count > 0)
        {
            var top = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            me.Hand.Add(top);
        }
    }
}
