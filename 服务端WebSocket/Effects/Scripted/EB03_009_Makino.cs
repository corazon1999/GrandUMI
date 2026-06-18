using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-009 卷乃（角色 / 炎 / 风车村）
/// 【启动主要】可以将此角色转为休息状态：本回合中，我方最多1张原本没有效果的角色力量+2000。
///
/// 实现说明：成本为横置自身；「原本没有效果」= EffectTags 与 Abilities 均为空。
/// </summary>
public class EB03_009_Makino : IScriptedEffect
{
    public string CardNumber => "EB03-009";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var cands = me.Characters.Where(c =>
            c.Info.EffectTags.Length == 0 && c.Info.Abilities.Length == 0).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卷乃【启动主要】：横置此角色，使我方最多1张原本没有效果的角色本回合力量+2000？");
        if (!use) return;

        // 成本：横置自身
        AtomicOps.RestCard(self);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多1张原本没有效果的角色，本回合力量+2000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, 2000);
        }
    }
}
