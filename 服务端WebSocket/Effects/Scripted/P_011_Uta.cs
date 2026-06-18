using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-011 乌塔（领航 / 炎 / FILM，cost5 power5000）
/// 【启动主要】【每回合1次】①（可以将费用区中 1 张咚!!转为休息状态）：
///   本回合中，我方最多1张原本没有效果的角色力量+2000。
///
/// 实现说明：
///   - 每回合1次；成本=将 1 张活跃咚!!转为休息状态（① = 咚!!×1）。
///   - 「原本没有效果」= EffectTags 与 Abilities 均为空。
/// </summary>
public class P_011_Uta : IScriptedEffect
{
    public string CardNumber => "P-011";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = "P-011-act";
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：将费用区 1 张活跃咚!!转为休息状态
        var activeDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (activeDon is null) return;

        var cands = me.Characters.Where(c =>
            c.Info.EffectTags.Length == 0 && c.Info.Abilities.Length == 0).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "乌塔【启动主要】：将 1 张咚!!转为休息状态，使我方最多 1 张原本没有效果的角色本回合力量+2000？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        // 支付成本：横置 1 张活跃咚!!
        activeDon.State = DonState.Rest;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多 1 张原本没有效果的角色，本回合力量+2000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, 2000);
        }
    }
}
