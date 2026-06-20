using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-060 佳妮法（角色）
/// 【攻击时】咚!!-1（可以将我方场上指定数量的咚!!放回咚!!卡组）：抽取 2 张卡牌，丢弃我方的 1 张手牌。
///
/// 实现说明：
///   - 可选成本咚!!-1（ReturnDonToDeck 1）；支付后抽 2 张，再弃 1 张手牌。
/// </summary>
public class OP03_060_Kalifa : IScriptedEffect
{
    public string CardNumber => "OP03-060";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (me.CostArea.Count < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "佳妮法【攻击时】：咚!!-1，抽 2 张并弃 1 张手牌？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        if (me.Hand.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方的 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var card = me.Hand.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, card);
    }
}
