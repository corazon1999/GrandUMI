using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-014 巴尔托洛梅奥（角色 / 炎 4 费 6000）
/// 此角色将要被 KO 的场合，可以改为丢弃我方手牌中的 1 张事件，使此角色不会被 KO。
/// 【登场时】将我方手牌中最多 1 张原本的费用不高于 3 且拥有《德莱斯罗兹》特征的事件发动。
///
/// 实现说明 / 简化点：
///   - 置换 KO 用 PreKO 钩子：可选丢弃 1 张手牌事件后 MarkPreventKO（参照 OP15-003 写法）。
///   - 【登场时】从手牌中选最多 1 张"原本费用≤3 且含《德莱斯罗兹》特征"的事件并发动，
///     用 AtomicOps.PlayEventFromHandFree 走事件结算流程。"原本的费用"取卡面 c.Info.Cost。
/// </summary>
public class OP15_014_Bartolomeo : IScriptedEffect
{
    public string CardNumber => "OP15-014";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.PreKO || t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.PreKO)
        {
            var events = me.Hand.Where(c => c.Info.Kind == CardKind.Event).ToList();
            if (events.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "巴尔托洛梅奥：是否丢弃 1 张事件以避免此角色被 KO？");
            if (!use) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardHand",
                "选择 1 张要丢弃的事件",
                events.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (chosen.Count == 0) return;

            var toDiscard = events.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.DiscardHand(me, toDiscard);
            ctx.State.MarkPreventKO(ctx.Source.Id);
            return;
        }

        // ── 【登场时】发动手牌中 1 张《德莱斯罗兹》且原本费用≤3 的事件 ──
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var cands = me.Hand.Where(c =>
                c.Info.Kind == CardKind.Event &&
                c.Info.Cost <= 3 &&
                c.Info.HasKeyword("德莱斯罗兹")
            ).ToList();
            if (cands.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandEvent",
                "将最多 1 张原本费用≤3 的《德莱斯罗兹》事件发动",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count == 0) return;

            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayEventFromHandFree(ctx.State, ctx.OwnerIndex, picked, ctx.Prompts);
        }
    }
}
