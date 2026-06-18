using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-018 滚!!（事件）
/// 【主要】将对方最多 2 张力量合计不高于 4000 的角色 KO。
///
/// 说明 / 简化点：
/// - "力量合计 ≤4000" 的多目标约束 DSL 无法表达，用脚本逐张选择并累计当前力量校验。
/// - 第 1 张：从当前力量 ≤4000 的对方角色中选 0~1 张；选中后 KO，并据剩余额度筛选第 2 张候选。
/// - 第 2 张候选限制为当前力量 ≤(4000 - 已选力量) 的对方角色，确保合计不超过 4000。
/// </summary>
public class OP09_018_Roll : IScriptedEffect
{
    public string CardNumber => "OP09-018";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        int budget = 4000;

        // 第 1 张：当前力量 ≤ 剩余额度
        var cand1 = opp.Characters
            .Where(c => ctx.State.CurrentPowerOf(oppIdx, c) <= budget)
            .ToList();
        if (cand1.Count == 0) return;

        var pick1 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择第 1 张要 KO 的对方角色（最多 2 张，力量合计≤4000）",
            cand1.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick1.Count == 0) return;

        var tgt1 = cand1.First(c => c.Id.ToString() == pick1[0]);
        int p1 = ctx.State.CurrentPowerOf(oppIdx, tgt1);
        AtomicOps.KO(ctx.State, oppIdx, tgt1);
        budget -= p1;

        // 第 2 张：在剩余额度内继续选（可放弃）
        var cand2 = opp.Characters
            .Where(c => c.Id != tgt1.Id && ctx.State.CurrentPowerOf(oppIdx, c) <= budget)
            .ToList();
        if (cand2.Count == 0) return;

        var pick2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择第 2 张要 KO 的对方角色（与第 1 张力量合计≤4000，可放弃）",
            cand2.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick2.Count > 0)
        {
            var tgt2 = cand2.First(c => c.Id.ToString() == pick2[0]);
            AtomicOps.KO(ctx.State, oppIdx, tgt2);
        }
    }
}
