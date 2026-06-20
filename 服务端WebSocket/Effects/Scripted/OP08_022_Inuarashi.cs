using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-022 犬岚（角色）
/// 【登场时】我方领袖拥有《纯毛族》特征的场合，对方最多2张处于休息状态且费用不高于5的角色，
///   在下个对方的重置阶段中不会转为活跃状态。
///
/// 实现说明 / 简化点：
///   - "不会转为活跃" 用 AtomicOps.PreventActivateNextReset 标记，引擎重置阶段对角色尊重该标记。
///   - 条件：我方领袖含《纯毛族》特征；目标为对方休息状态且 Info.Cost ≤ 5 的角色，最多 2 张。
/// </summary>
public class OP08_022_Inuarashi : IScriptedEffect
{
    public string CardNumber => "OP08-022";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方领袖拥有《纯毛族》特征
        if (!me.Leader.Info.HasKeyword("纯毛族")) return;

        var cands = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 5).ToList();
        if (cands.Count == 0) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多2张休息状态且费用≤5的角色，使其在下个对方重置阶段不转为活跃",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 2);
        foreach (var id in pick)
        {
            var c = cands.First(x => x.Id.ToString() == id);
            AtomicOps.PreventActivateNextReset(c);
        }
    }
}
