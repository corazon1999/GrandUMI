using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-043 撒谎布（角色，蓝，德莱斯罗兹/草帽一伙，力量1000）
/// 【阻挡者】（纯关键词，引擎处理）
/// 【KO时】可以将我方1张拥有《德莱斯罗兹》特征的领袖或舞台转为休息状态：
///   将对方最多1张费用不高于5的角色放回其持有者的手牌。
///
/// 实现：可选成本（横置我方《德莱斯罗兹》活跃领袖/舞台）+ 收益（对方≤1张费用≤5角色回手）。
///   - DSL triggers 节无激活成本通道，故脚本接管（原 DSL OP16-043 仅实现收益，已被本脚本按 trigger 优先覆盖）。
///   - "可以"=可选：成本选择 min 0，放弃则不发动。
/// </summary>
public class OP16_043_Usopp : IScriptedEffect
{
    public string CardNumber => "OP16-043";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        // 收益目标：对方费用不高于 5 的角色；无目标则效果无意义，直接不发动
        var benefit = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(oppIdx, c) <= 5)
            .ToList();
        if (benefit.Count == 0) return;

        // 成本候选：我方《德莱斯罗兹》特征、活跃（未横置）的 领袖 / 舞台
        var cost = new List<CardInstance>();
        if (!me.Leader.IsTapped && me.Leader.Info.HasKeyword("德莱斯罗兹"))
            cost.Add(me.Leader);
        if (me.StageCard is not null && !me.StageCard.IsTapped && me.StageCard.Info.HasKeyword("德莱斯罗兹"))
            cost.Add(me.StageCard);
        if (cost.Count == 0) return; // 付不起成本

        // "可以"=可选：先选成本（min 0 → 放弃则不发动）
        var cp = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrStage",
            "可以将我方1张《德莱斯罗兹》领袖或舞台转为休息状态作为成本（放回对方1张费用不高于5的角色）",
            cost.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (cp.Count < 1) return;
        AtomicOps.RestCard(cost.First(c => c.Id.ToString() == cp[0])); // 支付成本：横置

        // 收益：对方最多 1 张费用≤5 角色放回其持有者手牌
        var tp = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterCostLe5",
            "将对方最多1张费用不高于5的角色放回其持有者的手牌",
            benefit.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (tp.Count < 1) return;
        AtomicOps.BounceToHand(ctx.State, oppIdx, benefit.First(c => c.Id.ToString() == tp[0]));
    }
}
