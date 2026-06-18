using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-007 萨波（角色）
/// 【登场时】将对方最多 2 张力量合计不高于 4000 的角色 KO。
///
/// 实现说明 / 简化点：
///   - "力量合计不高于 4000" 是跨多目标的合计上限约束，单次 ChooseCards 无法表达，
///     故拆为两次顺序选择：先选第 1 张（力量≤4000 的角色），再在剩余预算内选第 2 张
///     （力量 ≤ 4000 - 第1张力量 的角色）。逐张满足后 KO，等价于合计 ≤4000。
///   - 力量用 Info.Power（卡面原始力量）评估，与文本"力量合计"一致。
///   - 对方角色 KO 用 AtomicOps.KO(state, 1 - OwnerIndex, tgt)。
/// </summary>
public class OP05_007_Sabo : IScriptedEffect
{
    public string CardNumber => "OP05-007";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        int budget = 4000;
        int picked = 0;

        while (picked < 2)
        {
            var cands = opp.Characters.Where(c => c.Info.Power <= budget).ToList();
            if (cands.Count == 0) break;

            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                $"选择 1 张对方角色 KO（剩余合计力量预算 {budget}，最多再选 {2 - picked} 张）",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count == 0) break;

            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            budget -= tgt.Info.Power;
            AtomicOps.KO(ctx.State, oppIdx, tgt);
            picked++;
        }
    }
}
