using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-016 马古拉（角色 / 炎 / 山贼）
/// 【登场时】本回合中，我方最多1张费用为1的红色（炎）角色力量+3000。
///
/// 实现说明：目标筛选「费用为1的红色角色」= c.Info.Cost == 1 && ColorList 含「炎」（领袖不计入）。
/// </summary>
public class OP02_016_Magra : IScriptedEffect
{
    public string CardNumber => "OP02-016";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Characters.Where(c =>
            c.Info.Cost == 1 && c.Info.ColorList.Contains("红")).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多1张费用为1的红色角色，本回合力量+3000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, 3000);
        }
    }
}
