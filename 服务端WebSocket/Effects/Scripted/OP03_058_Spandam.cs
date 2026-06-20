using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-058 阿斯巴古（领航）
/// 此领袖无法攻击。（卡面含"此领袖无法攻击"，由引擎自动禁攻）
/// 【启动主要】咚!!-1，可以将此领袖转为休息状态：
///   将我方手牌中最多 1 张费用不高于 5 且拥有《卡雷拉公司》特征的角色卡牌登场。
///
/// 实现说明：
///   - 成本：咚!!-1（ReturnDonToDeck 1）+ 横置领袖（RestCard）。仅当有合法目标且领袖活跃时询问发动。
///   - 可选(用 ConfirmOptional)，支付成本后从手牌登场所选角色。
/// </summary>
public class OP03_058_Spandam : IScriptedEffect
{
    public string CardNumber => "OP03-058";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 领袖须为活跃（可横置作为成本）且至少有 1 咚可放回
        if (me.Leader.IsTapped) return;
        if (me.CostArea.Count < 1) return;

        // 候选：手牌中费用≤5 且《卡雷拉公司》的角色
        var cands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Power >= 0 &&
            ctx.State.CurrentCostOf(ctx.OwnerIndex, c) <= 5 &&
            c.Info.HasKeyword("卡雷拉公司")).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿斯巴古【启动主要】：咚!!-1 并横置领袖，将手牌中 1 张费用≤5 的《卡雷拉公司》角色登场？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多 1 张费用≤5 的《卡雷拉公司》角色登场",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        // 支付成本：咚!!-1 + 横置领袖
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        AtomicOps.RestCard(me.Leader);

        var picked = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
    }
}
