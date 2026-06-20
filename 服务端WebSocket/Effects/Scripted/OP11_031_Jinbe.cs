using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-031 甚平（角色）
/// 【登场时】我方领袖拥有《鱼人族》或《人鱼族》特征的场合，将对方最多 1 张费用不高于 5 的角色转为休息状态。
/// 【启动主要】【每回合1次】我方最多 1 张拥有《鱼人族》或《人鱼族》特征的角色可以在登场的回合中攻击角色。
///
/// 实现说明 / 简化点：
///   - 仅实现可表达的【登场时】部分：领袖具《鱼人族》/《人鱼族》时，横置对方最多 1 张费用≤5 的角色。
///   - 【启动主要】"在登场回合中攻击角色"属于召唤病/可攻击角色的特殊状态机制，引擎无对应通道，未实现。
/// </summary>
public class OP11_031_Jinbe : IScriptedEffect
{
    public string CardNumber => "OP11-031";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        // 条件：我方领袖拥有《鱼人族》或《人鱼族》特征
        bool ok = me.Leader.Info.HasKeyword("鱼人族") || me.Leader.Info.HasKeyword("人鱼族");
        if (!ok) return;

        // 将对方最多 1 张费用不高于 5 的角色转为休息状态
        var cands = opp.Characters.Where(c => c.Info.Cost <= 5).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张费用不高于 5 的角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
