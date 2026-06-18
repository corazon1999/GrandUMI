using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-001 萨波（领袖 / 炎・地 / 5000 / 革命军）
/// 【咚!!×1】【对方的回合中】【每回合1次】我方力量5000或更高的角色将要被KO的场合，
///   该角色可以在本回合中力量-1000，以代替被KO。
///
/// 实现说明：
///   - 用 OnAllyWillBeKOd 守护者钩子（仅战斗KO时派发）。ctx.OwnerIndex 即被KO卡所属方 = 我方。
///   - 发动条件：领袖被赋予中的咚!! ≥1（【咚!!×1】）、对方回合中、每回合1次。
///   - 置换：被守护的角色当前力量≥5000，可选发动；发动则该角色本回合力量-1000，
///     并调用 MarkPreventKO 取消本次KO。
///   - 局限：守护仅在战斗KO时派发；效果KO/离场的置换不在此覆盖范围。
/// </summary>
public class OP05_001_Sabo : IScriptedEffect
{
    public string CardNumber => "OP05-001";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source; // 领袖萨波

        // 【对方的回合中】
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;

        // 【咚!!×1】：领袖需有 ≥1 张被赋予中的咚!!
        if (me.AttachedDonCount(self.Id) < 1) return;

        // 【每回合1次】
        var key = self.Info.Number + "-guard" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 取出被守护的目标
        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        if (victimId is null) return;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victim is null) return;

        // 该角色当前力量需 ≥5000
        if (ctx.State.CurrentPowerOf(ctx.OwnerIndex, victim) < 5000) return;

        // 可选发动
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "萨波：将该角色本回合力量-1000，以代替被KO？");
        if (!use) return;

        AtomicOps.AddPowerThisTurn(victim, -1000);
        ctx.State.MarkPreventKO(victim.Id);
        me.TurnOnceUsed.Add(key);
    }
}
