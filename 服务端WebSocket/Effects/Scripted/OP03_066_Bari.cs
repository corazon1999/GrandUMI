using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-066 巴利（角色）
/// 【登场时】➁（可以将费用区中 2 张咚!! 转为休息状态）：从咚!!卡组追加最多 1 张活跃状态的咚!!。
///   之后，我方场上存在 8 张或更多咚!! 的场合，将对方最多 1 张费用不高于 4 的角色 KO。
///
/// 说明：➁ 为可选额外成本（"可以"），需 ≥2 张活跃咚；先 ConfirmOptional 询问。
/// "我方场上存在 8 张或更多咚!!"取费用区咚!!总数 TotalDonInCostArea（含活跃/休息/被赋予中）。
/// 注意：先支付成本(休息2咚→TotalDonInCostArea不变)再追加1咚，故判 8 张取成本后即时值。
/// </summary>
public class OP03_066_Bari : IScriptedEffect
{
    public string CardNumber => "OP03-066";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 可选额外成本：将 2 张活跃咚转为休息
        if (me.ActiveDonCount < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "巴利【登场时】：将费用区 2 张咚!! 转为休息，追加最多 1 张活跃咚!!（场上≥8咚时再 KO 对方 1 张费用≤4 的角色）？");
        if (!use) return;

        // 支付成本：2 张活跃咚 → 休息
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 2) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; rested++; }
        }
        if (rested < 2) return;

        // 效果 1：追加最多 1 张活跃咚
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);

        // 效果 2：场上存在 8 张或更多咚!! 时，KO 对方最多 1 张费用≤4 的角色
        if (me.TotalDonInCostArea < 8) return;
        var targets = opp.Characters.Where(c => c.Info.Cost <= 4).ToList();
        if (targets.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张费用≤4 的角色 KO",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
