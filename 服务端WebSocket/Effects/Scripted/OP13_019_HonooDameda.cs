using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-019 但『火焰』可饶不过你啊!!（事件）
/// 【主要】可以将我方的 4 张咚!!转为休息状态：
///   本回合中，对方最多 1 张角色力量 -3000。
///   之后，将对方最多 1 张力量不高于 3000 的角色 KO。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 实现说明 / 简化点：
///   - "可以将 4 张咚转为休息" 是可选额外费用：需存在 4 张活跃咚才可支付；
///     不支付则跳过整个【主要】效果（力量 -3000 与 KO 均不发生）。
///   - 力量 -3000 与 KO 的目标均为"最多 1 张"（min=0 可跳过）。
///   - KO 的候选为对方"当前力量不高于 3000"的角色（在 -3000 生效之后再判定）。
/// </summary>
public class OP13_019_HonooDameda : IScriptedEffect
{
    public string CardNumber => "OP13-019";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 【反击】本次战斗中，我方领袖力量 +3000
            var me0 = ctx.State.Players[ctx.OwnerIndex];
            AtomicOps.AddPowerThisBattle(me0.Leader, 3000);
            return;
        }

        // 【主要】
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 额外费用：将我方 4 张活跃咚!!转为休息状态（可选）
        int activeDon = me.CostArea.Count(d => d.State == DonState.Active);
        if (activeDon < 4) return; // 无法支付费用 → 不发动

        bool pay = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "将我方的 4 张咚!!转为休息状态，发动【主要】效果？");
        if (!pay) return;

        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 4) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; rested++; }
        }

        // 本回合中，对方最多 1 张角色力量 -3000
        var minusCands = opp.Characters.ToList();
        if (minusCands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张角色，本回合力量 -3000",
                minusCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var target = minusCands.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
                if (target is not null) AtomicOps.AddPowerThisTurn(target, -3000);
            }
        }

        // 之后：将对方最多 1 张力量不高于 3000 的角色 KO（在 -3000 生效后判定）
        int oppIdx = 1 - ctx.OwnerIndex;
        var koCands = opp.Characters
            .Where(c => ctx.State.CurrentPowerOf(oppIdx, c) <= 3000)
            .ToList();
        if (koCands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张力量不高于 3000 的角色 KO",
                koCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var target = koCands.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
                if (target is not null) AtomicOps.KO(ctx.State, oppIdx, target);
            }
        }
    }
}
