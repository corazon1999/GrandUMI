using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-033 佩罗娜（角色）
/// 【登场时】直到下个对方的结束阶段结束时为止，对方最多 2 张费用不高于 5 的角色无法转为休息状态。
/// 【KO时】可以将我方的 1 张卡牌转为休息状态：将我方手牌中最多 1 张费用不高于 5 的绿色角色卡牌登场。
///
/// 实现说明 / 简化点：
///   - 【登场时】对所选对方角色施加 RestrictionKind.CannotBeRested，持续期
///     KeywordDuration.UntilNextOpponentEndPhase（即"直到下个对方结束阶段"）。RestCard 对被标记者 no-op。
///   - 【KO时】可选效果：成本为将我方 1 张活跃卡牌(领袖/角色)转为休息状态；收益为从手牌登场
///     1 张费用≤5 的绿色("绿")角色。绿色在本卡库中对应元素色 "绿"。
///   - "费用不高于" 用 ctx.State.CurrentCostOf(side, card) 评估（含持续费用修正）。
/// </summary>
public class OP14_033_Perona : IScriptedEffect
{
    public string CardNumber => "OP14-033";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 对方费用≤5 的角色 → 选最多 2 张施加"无法转为休息状态"
            var cands = opp.Characters
                .Where(c => ctx.State.CurrentCostOf(oppIdx, c) <= 5)
                .ToList();
            if (cands.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 2 张费用≤5 的对方角色，使其无法转为休息状态",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 2);

            foreach (var cid in chosen)
            {
                var tgt = cands.First(c => c.Id.ToString() == cid);
                AtomicOps.AddRestriction(tgt, RestrictionKind.CannotBeRested,
                    KeywordDuration.UntilNextOpponentEndPhase);
            }
            return;
        }

        // ── OnKO：可选，成本为横置我方 1 张活跃卡牌 ──
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.ColorList.Contains("绿") &&
            ctx.State.CurrentCostOf(ctx.OwnerIndex, c) <= 5
        ).ToList();
        if (playable.Count == 0) return;

        var costCands = new List<CardInstance>();
        if (!me.Leader.IsTapped) costCands.Add(me.Leader);
        costCands.AddRange(me.Characters.Where(c => !c.IsTapped));
        if (costCands.Count == 0) return; // 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "佩罗娜【KO时】：将我方 1 张卡牌转为休息状态，登场 1 张费用≤5 的绿色角色？");
        if (!use) return;

        // 成本：将我方 1 张活跃卡牌转为休息状态
        var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "将我方 1 张卡牌转为休息状态（成本）",
            costCands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (costPick.Count == 0) return;
        var costCard = costCands.First(c => c.Id.ToString() == costPick[0]);
        AtomicOps.RestCard(costCard);

        // 收益：从手牌登场最多 1 张费用≤5 的绿色角色
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosenPlay = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场最多 1 张费用≤5 的绿色角色",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosenPlay.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosenPlay[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
