using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-029 霜月耕三郎（3 费 2000）
/// 【登场时】将对方最多 1 张费用不高于 2 的角色转为休息状态。
///   之后，将对方最多 1 张处于休息状态且原本的费用不高于 1 的角色 KO。
///
/// 说明：
///   - 第一步候选 = 对方费用（原本费用）≤2 的角色，最多选 1 张转休息（可放弃）。
///   - 第二步候选 = 对方处于休息状态且原本费用 ≤1 的角色（含本次刚被转休息者），
///     最多选 1 张 KO（可放弃）。
/// 两步均为"最多 1 张"，故 min=0 可跳过。
/// </summary>
public class OP12_029_Kozaburo : IScriptedEffect
{
    public string CardNumber => "OP12-029";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 第一步：将对方最多 1 张费用 ≤2 的角色转为休息状态
        var restTargets = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
        if (restTargets.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张费用不高于 2 的角色转为休息状态",
                restTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var target = restTargets.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
                if (target is not null) AtomicOps.RestCard(target);
            }
        }

        // 第二步：将对方最多 1 张处于休息状态且原本费用 ≤1 的角色 KO
        var koTargets = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 1).ToList();
        if (koTargets.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCharacter",
                "将对方最多 1 张休息状态且原本费用不高于 1 的角色 KO",
                koTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var target = koTargets.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
                if (target is not null) AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, target);
            }
        }
    }
}
