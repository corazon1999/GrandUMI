using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-091 希路麦波（角色 / 地 / 海军）
/// 【登场时】本回合中，将对方最多 1 张原本没有效果的角色的费用变为 0。
///
/// 实现说明：
///   - "原本没有效果的角色"：卡面无任何效果触发且无能力关键词，判 EffectTags 与 Abilities 均为空。
///   - "费用变为 0"：取目标当前费用 CurrentCostOf，施加 -当前费用 的本回合费用修正（近似变为 0）。
/// </summary>
public class OP03_091_Hillmappoo : IScriptedEffect
{
    public string CardNumber => "OP03-091";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        bool NoEffect(CardInstance c) =>
            c.Info.EffectTags.Length == 0 && c.Info.Abilities.Length == 0;

        var cands = opp.Characters.Where(NoEffect).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张原本没有效果的角色的费用变为 0（本回合）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        int cur = ctx.State.CurrentCostOf(oppIdx, tgt);
        if (cur > 0)
            AtomicOps.AddCostModifier(tgt, -cur, KeywordDuration.ThisTurn);
    }
}
