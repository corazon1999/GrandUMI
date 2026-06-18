using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-111 布拉哈姆（角色）
/// 【启动主要】【每回合1次】可以将 1 张费用为 1 的舞台放回其持有者的卡组最下方：
///   将对方最多 1 张费用不高于 4 的角色转为休息状态。
///
/// 实现说明 / 简化点：
///   - 发动成本为"将 1 张费用为 1 的舞台放回其持有者的卡组最下方"。该成本不在 DSL 既有 cost 集合内，
///     故用脚本实现：在我方场上费用为 1 的舞台中选 1 张放回卡组底（含我方舞台；本套以我方舞台为目标）。
///   - 无可作为成本的费用 1 舞台时不发动。
///   - 【触发】"我方生命≤2 时此卡牌登场"为条件性从生命登场的特殊机制，不在本脚本范围内（仅实现启动主要）。
/// </summary>
public class OP06_111_Braham : IScriptedEffect
{
    public string CardNumber => "OP06-111";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合 1 次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本候选：我方场上费用为 1 的舞台
        var stages = new List<CardInstance>();
        if (me.StageCard != null && me.StageCard.Info.Cost == 1) stages.Add(me.StageCard);
        if (stages.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "布拉哈姆【启动主要】：将 1 张费用 1 的舞台放回卡组最下方，将对方 1 张费用≤4 的角色转为休息？");
        if (!use) return;

        // 选择作为成本放回卡组底的费用 1 舞台
        var stagePick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnStage",
            "选择 1 张费用 1 的舞台放回卡组最下方（成本）",
            stages.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (stagePick.Count < 1) return; // 未支付成本

        var stage = stages.First(c => c.Id.ToString() == stagePick[0]);
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, stage);

        me.TurnOnceUsed.Add(key);

        // 效果：将对方最多 1 张费用不高于 4 的角色转为休息状态
        var cands = opp.Characters.Where(c => c.Info.Cost <= 4).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤4 的角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
