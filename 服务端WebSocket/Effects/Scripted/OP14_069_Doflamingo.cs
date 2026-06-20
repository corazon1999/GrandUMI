using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-069 堂吉诃德·多弗拉门戈（角色 / 暗 / 10 费 / 10000 / 王下七武海·堂吉诃德海盗团）
/// 【登场时】咚!!-3：选择以下的 1 项。
///   ・我方领袖拥有《堂吉诃德海盗团》特征的场合，将对方最多 1 张费用不高于 8 的角色 KO。
///   ・直到下个对方的结束阶段结束时为止，对方最多 3 张费用不高于 7 的角色无法转为休息状态。
///
/// 实现说明 / 简化点：
///   - 成本"咚!!-3"= 将费用区 3 张咚!!放回咚!!卡组(ReturnDonToDeck)，需费用区 ≥3 张。
///     成本必付，故无 ConfirmOptional；"二选一"用 ChooseOption。
///   - 第 1 项的"我方领袖拥有《堂吉诃德海盗团》特征"为该项的发动前提，不满足则该项无收益。
///   - KO 用 AtomicOps.KO(对方下标)；"无法转为休息状态"用 RestrictionKind.CannotBeRested
///     (KeywordDuration.UntilNextOpponentEndPhase)。费用按当前费用 CurrentCostOf 评估。
/// </summary>
public class OP14_069_Doflamingo : IScriptedEffect
{
    public string CardNumber => "OP14-069";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本：咚!!-3（费用区需 ≥3 张咚!!）
        if (me.TotalDonInCostArea < 3) return;
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 3)) return;

        // 二选一
        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "多弗拉门戈【登场时】：选择 1 项",
            new[]
            {
                "我方领袖为《堂吉诃德海盗团》时，将对方 1 张费用≤8 的角色 KO",
                "对方最多 3 张费用≤7 的角色直到下个对方结束阶段无法转为休息状态",
            });

        if (opt == 0)
        {
            // 第 1 项：领袖需拥有《堂吉诃德海盗团》特征
            if (!me.Leader.Info.HasKeyword("堂吉诃德海盗团")) return;

            var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 8).ToList();
            if (cands.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张费用≤8 的角色 KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }
        else
        {
            // 第 2 项：对方最多 3 张费用≤7 的角色无法转为休息状态
            var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 7).ToList();
            if (cands.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 3 张费用≤7 的角色，使其无法转为休息状态",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 3);
            foreach (var id in chosen)
            {
                var tgt = cands.FirstOrDefault(c => c.Id.ToString() == id);
                if (tgt != null)
                    AtomicOps.AddRestriction(tgt, RestrictionKind.CannotBeRested, KeywordDuration.UntilNextOpponentEndPhase);
            }
        }
    }
}
