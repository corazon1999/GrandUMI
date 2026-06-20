using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-077 夏洛特·玲玲（领航 / 地·光 / 四皇·大妈海盗团）
/// 【咚‼×2】【攻击时】②（可以将费用区中 2 张活跃咚转为休息状态），可以丢弃我方的 1 张手牌：
///   我方生命卡牌不多于 1 张的场合，将卡组最上方的最多 1 张卡牌加入生命区最上方。
///
/// 实现说明：
///   - 发动条件【咚‼×2】：本卡（领袖）被赋予中的咚!! ≥2。
///   - 成本 ②：将费用区 2 张活跃咚转为休息状态；活跃咚不足 2 则无法支付。
///   - 强耦合成本 弃 1 手牌：与②同为发动成本，须有手牌且支付后才结算收益。
///   - "可以"=可选：先 ConfirmOptional 询问；条件不足直接 return。
///   - 收益：仅当我方生命 ≤1 时，AddLifeFromDeckTop(1)。
/// </summary>
public class OP03_077_CharlotteLinlin : IScriptedEffect
{
    public string CardNumber => "OP03-077";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚‼×2】发动条件：本卡被赋予中的咚!! ≥2
        if (me.AttachedDonCount(self.Id) < 2) return;
        // 成本前置检查：需 2 张活跃咚 + 至少 1 张手牌
        if (me.ActiveDonCount < 2) return;
        if (me.Hand.Count < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "玲玲【攻击时】：将 2 张活跃咚转为休息状态并丢弃 1 张手牌，若我方生命≤1 则将卡组顶 1 张加入生命？");
        if (!use) return;

        // 成本②：将 2 张活跃咚转为休息状态
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 2) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; rested++; }
        }
        if (rested < 2) return; // 支付失败保护

        // 成本：丢弃我方 1 张手牌
        var discard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
            "丢弃 1 张手牌作为成本", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (discard.Count < 1) return;
        var dc = me.Hand.First(c => c.Id.ToString() == discard[0]);
        AtomicOps.DiscardHand(me, dc);

        // 收益：我方生命 ≤1 时，将卡组顶最多 1 张加入生命区最上方
        if (me.LifeCount <= 1)
            AtomicOps.AddLifeFromDeckTop(me, 1);
    }
}
