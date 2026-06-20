using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-035 费雪·泰格（角色 / 鱼人族·鱼人岛·太阳海盗团）
/// 当此角色因对方的效果而被KO时，可以将我方的 1 张咚!! 转为休息状态。那样做的场合，
///   将我方手牌中最多 1 张费用不高于 4 且拥有《鱼人族》或《人鱼族》特征的角色卡牌登场。  → complex（见下）
/// 【登场时】将对方最多 1 张角色转为休息状态。  → 本脚本实现此段
///
/// 实现说明 / 简化点：
///   - 仅实现【登场时】段：将对方最多 1 张角色转为休息状态（RestCard）。强制（无"可以"），
///     但目标可选最多 1 张（min=0）。
///   - "因对方效果被KO时"的 OnKO 段：触发来源（是否因对方效果）无对应钩子区分，且以横置咚为成本
///     从手牌登场角色，成本与结果强耦合，OnKO 无法表达，该段省略，整体登场段独立实现。
/// </summary>
public class OP11_035_FisherTiger : IScriptedEffect
{
    public string CardNumber => "OP11-035";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 【登场时】将对方最多 1 张角色转为休息状态
        var cands = opp.Characters.Where(c => !c.IsTapped).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选最多 1 张对方角色，转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = cands.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is not null) AtomicOps.RestCard(target);
    }
}
