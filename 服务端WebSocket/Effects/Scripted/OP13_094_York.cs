using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-094 约克（地 / 艾格赫德 / 科学家 / 1 费 1000）
/// 【登场时】本回合中，我方最多 1 张拥有《天龙人》特征的角色力量 +2000。
///
/// 说明：
///   - 候选 = 我方场上拥有《天龙人》特征的角色；最多 1 张（min=0，可不选）。
///   - 选中后，本回合该角色力量 +2000。
/// </summary>
public class OP13_094_York : IScriptedEffect
{
    public string CardNumber => "OP13-094";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 候选：我方场上拥有《天龙人》特征的角色
        var candidates = me.Characters
            .Where(c => c.Info.HasKeyword("天龙人"))
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多 1 张拥有《天龙人》特征的角色，本回合力量 +2000（可不选）",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var picked = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (picked is not null) AtomicOps.AddPowerThisTurn(picked, 2000);
    }
}
