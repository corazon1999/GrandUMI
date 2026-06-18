using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-086 琴拉米（角色 / 地 / 2 费 / 3000 / 百兽海盗团·SMILE）
/// 【登场时】对方场上存在费用为 0 的角色的场合，抽取 2 张卡牌，丢弃我方的 2 张手牌。
///
/// 说明 / 简化点：
///   - 条件"对方场上存在费用为 0 的角色"用 opp.Characters 按 CurrentCost() 判定。
///   - 抽 2 张后丢 2 张手牌：丢弃为强制效果，由玩家从手牌中选 2 张丢弃（不足 2 张则全丢）。
/// </summary>
public class OP08_086_Kinlami : IScriptedEffect
{
    public string CardNumber => "OP08-086";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：对方场上存在费用为 0 的角色
        if (!opp.Characters.Any(c => ctx.State.CurrentCostOf(c) == 0)) return;

        // 抽取 2 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        // 丢弃我方 2 张手牌（不足则全部丢弃）
        int n = Math.Min(2, me.Hand.Count);
        if (n == 0) return;

        var picks = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方 2 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), n, n);
        foreach (var id in picks)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.DiscardHand(me, card);
        }
    }
}
