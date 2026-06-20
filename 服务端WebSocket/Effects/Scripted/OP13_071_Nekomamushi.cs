using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-071 猫蝮蛇（3 费 3000，纯毛族/罗杰海盗团）
/// 【登场时】我方场上存在 8 张或更多咚!!的场合，
///   将对方最多 1 张原本的力量不高于 3000 的角色 KO。
///
/// 实现：登场时触发。条件 = 我方费用区（含活跃/休息/被赋予）合计咚!! ≥ 8。
/// 候选 = 对方原本力量（印刷力量 Info.Power）不高于 3000 的角色，可选取最多 1 张 KO（可不选）。
/// </summary>
public class OP13_071_Nekomamushi : IScriptedEffect
{
    public string CardNumber => "OP13-071";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方场上存在 8 张或更多咚!!（费用区合计）
        if (me.TotalDonInCostArea < 8) return;

        // 候选：对方原本力量不高于 3000 的角色
        var candidates = opp.Characters
            .Where(c => c.Info.Power <= 3000)
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张原本力量不高于 3000 的角色 KO",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null)
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, target);
    }
}
