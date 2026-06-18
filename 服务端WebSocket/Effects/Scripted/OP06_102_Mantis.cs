using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-102 螳螂（角色 / 光 / 空岛・山迪亚战士）
/// 【启动主要】【每回合1次】可以将 1 张费用为 1 的舞台放回其持有者的卡组最下方：
///   将对方最多 1 张费用不高于 2 的角色 KO。
///
/// 说明 / 简化点：
///   - 成本"将 1 张费用为 1 的舞台放回卡组底"不在 DSL cost 集合内，故脚本实现。
///   - 舞台可为任意一方持有；玩家从场上所有费用 1 的舞台中选择 1 张放回其持有者卡组底。
///   - 【每回合1次】用 TurnOnceUsed 标记。"可以"=可选，需有合法成本与目标才提示。
/// </summary>
public class OP06_102_Mantis : IScriptedEffect
{
    public string CardNumber => "OP06-102";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本候选：场上费用为 1 的舞台（任意一方持有）
        var stages = new List<(CardInstance stage, int ownerIdx)>();
        if (me.StageCard is { } myStage && myStage.Info.Cost == 1) stages.Add((myStage, ctx.OwnerIndex));
        if (opp.StageCard is { } oppStage && oppStage.Info.Cost == 1) stages.Add((oppStage, 1 - ctx.OwnerIndex));
        if (stages.Count == 0) return;

        // KO 目标：对方费用不高于 2 的角色
        var targets = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
        if (targets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "螳螂【启动主要】：将 1 张费用 1 的舞台放回卡组底，KO 对方 1 张费用≤2 的角色？");
        if (!use) return;

        // 成本：选择并将 1 张费用 1 的舞台放回其持有者卡组底
        var stageExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = stages.Select(s => new { id = s.stage.Id.ToString(), number = s.stage.Info.Number }).ToList(),
        };
        var stageChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Stage",
            "选择 1 张费用为 1 的舞台放回其持有者卡组最下方",
            stages.Select(s => s.stage.Id.ToString()).ToList(), 1, 1, stageExtra);
        if (stageChoice.Count == 0) return; // 未支付成本
        var picked = stages.First(s => s.stage.Id.ToString() == stageChoice[0]);
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, picked.ownerIdx, picked.stage);

        me.TurnOnceUsed.Add(key);

        // 效果：KO 对方最多 1 张费用≤2 的角色
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择 1 张要 KO 的对方角色（费用≤2）",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
