using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-025 狮子里昂（角色）
/// 【登场时】对方最多1张处于休息状态且费用不高于3的角色，
///   在下个对方的重置阶段中不会转为活跃状态。
///
/// 实现说明 / 简化点：
///   - 用 AtomicOps.PreventActivateNextReset 标记最多 1 张对方休息状态且 Info.Cost ≤ 3 的角色。
/// </summary>
public class OP08_025_LionLeon : IScriptedEffect
{
    public string CardNumber => "OP08-025";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var cands = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 3).ToList();
        if (cands.Count == 0) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多1张休息状态且费用≤3的角色，使其在下个对方重置阶段不转为活跃",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        foreach (var id in pick)
        {
            var c = cands.First(x => x.Id.ToString() == id);
            AtomicOps.PreventActivateNextReset(c);
        }
    }
}
