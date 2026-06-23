using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-091 玛卡斯·玛兹圣（6 费 5000，天龙人/五老星）
/// 原文：
///   我方废弃区中有 7 张或更多卡牌的场合，此角色不会因对方的效果而离场，并获得【阻挡者】效果。
///   【登场时】可以丢弃我方的 1 张手牌：将对方最多 1 张原本的费用不高于 5 的角色 KO。
///
/// 本脚本实现【登场时】部分：
///   - 可选效果（"可以"）：先询问是否发动；
///   - 成本：丢弃我方 1 张手牌（玩家自选，需手牌 ≥1）；
///   - 效果：将对方最多 1 张"原本的费用不高于 5"（Info.Cost ≤ 5）的角色 KO（min=0，可不选）。
///
/// 持续光环（登场时注册 ContinuousEffect）：
///   "废弃区≥7 时此角色获得【阻挡者】，且不会因效果离场" 由 GrantKeyword="阻挡者"+LeaveGuard="effect"
///   实现（引擎粒度为"任意效果"，略宽于卡面"对方的效果"），按废弃区张数动态评估。
/// </summary>
public class OP13_091_MarsGod : IScriptedEffect
{
    public string CardNumber => "OP13-091";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        int owner = ctx.OwnerIndex;

        // ── 持续光环：我方废弃区≥7 → 此角色获得【阻挡者】，且不会因效果离场（登场时无条件注册）──
        var selfId0 = ctx.Source.Id;
        s.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId0.ToString());
        s.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId0.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            LeaveGuard = "effect",   // 引擎粒度为"任意效果离场"，略宽于卡面"对方的效果"
            Predicate = (st, side, card) => card.Id == selfId0 && st.Players[owner].Trash.Count >= 7,
        });

        var me = s.Players[owner];
        int oppIdx = 1 - owner;
        var opp = s.Players[oppIdx];

        // 成本需要：我方手牌 ≥1 才能丢弃（【登场时】可选效果）
        if (me.Hand.Count < 1) return;

        // 候选：对方"原本的费用不高于 5"的角色
        var targets = opp.Characters.Where(c => c.Info.Cost <= 5).ToList();

        // 可选效果（"可以…"）：询问是否发动
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "玛卡斯·玛兹圣【登场时】：丢弃我方 1 张手牌，将对方最多 1 张原本费用≤5 的角色 KO？");
        if (!use) return;

        // 支付成本：丢弃我方 1 张手牌（玩家自选）
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var discarded = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Mars091Cost",
            "丢弃我方 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (discarded.Count == 0) return; // 未支付成本 → 不发动
        var costCard = me.Hand.FirstOrDefault(c => c.Id.ToString() == discarded[0]);
        if (costCard is null) return;
        AtomicOps.DiscardHand(me, costCard);

        // 效果：KO 对方最多 1 张原本费用≤5 的角色
        if (targets.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张原本费用≤5 的角色 KO（可不选）",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;
        var ko = targets.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.KO(s, oppIdx, ko);
    }
}
