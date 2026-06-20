using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-041 山智（领航 / 水·暗）
/// 【启动主要】【每回合1次】咚!!-1：将我方手牌中最多1张原本的费用不高于3且拥有《草帽一伙》特征的事件发动。
/// 【攻击时】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，从咚!!卡组中追加最多1张休息状态的咚!!。
///
/// 实现说明：
///   - 启动主要的"咚!!-1"成本与"每回合1次"由脚本自行支付/校验（引擎不代扣）。
///     活跃咚不足 1 时无法支付，直接返回。
///   - "发动事件"调用 AtomicOps.PlayEventFromHandFree（移出手牌→入废弃区→解析其【主要】效果）。
///   - 候选事件经 extra.choiceCards 下发卡面（手牌私有区域，前端默认看不到身份）。
///   - 攻击时追加咚为"最多1张"，此处实现为追加 1 张（条件满足且咚卡组非空时）。
/// </summary>
public class OP12_041_Sanji : IScriptedEffect
{
    public string CardNumber => "OP12-041";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.ActivatedMain || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.ActivatedMain)
            await ResolveActivated(ctx);
        else if (ctx.Trigger == EffectTrigger.OnAttackDeclare)
            ResolveOnAttack(ctx);
    }

    // 【启动主要】【每回合1次】咚!!-1：发动手牌中费用≤3的《草帽一伙》事件
    private async Task ResolveActivated(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 每回合 1 次
        var key = "OP12-041-Activated" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 候选：手牌中原本费用 ≤3 且拥有《草帽一伙》特征的事件
        var candidates = me.Hand
            .Where(c => c.Info.Kind == CardKind.Event
                        && c.Info.Cost <= 3
                        && c.Info.HasKeyword("草帽一伙"))
            .ToList();
        if (candidates.Count == 0) return;

        // 成本：咚!!-1（需至少 1 张可放回的咚）
        if (me.CostArea.Count < 1) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandEvent",
            "选最多 1 张费用≤3的《草帽一伙》事件发动",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var card = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (card is null) return;

        // 支付咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        me.TurnOnceUsed.Add(key);
        AtomicOps.PlayEventFromHandFree(ctx.State, ctx.OwnerIndex, card, ctx.Prompts);
    }

    // 【攻击时】我方咚数≤对方咚数 → 从咚卡组追加最多 1 张休息咚
    private static void ResolveOnAttack(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        if (me.TotalDonInCostArea > opp.TotalDonInCostArea) return;
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }
}
