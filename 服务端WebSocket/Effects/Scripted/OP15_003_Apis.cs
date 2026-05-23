using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-003 爱比达（置换 KO 效果示例）
/// 此角色将要被 KO 的场合，可以改为丢弃我方手牌中 1 张力量不高于 6000 的角色卡牌，使此角色不会被 KO。
///
/// 置换效果通过 OnKO 触发，在 BattleEngine 调用 KOCard 之前应给该卡一次"置换"机会。
/// 当前 M5 实现：在 OnKO 时机询问玩家是否使用置换；若使用则取消 KO（把卡留在场上）。
///
/// 注：要让该效果真正"取消 KO"需要修改 BattleEngine 流程为带返回值的 PreKO 钩子。
/// 此处仅演示置换机制的注册形式；完整实现需在 BattleEngine.KOCard 之前先调用 PreKO 钩子。
/// </summary>
public class OP15_003_Apis : IScriptedEffect
{
    public string CardNumber => "OP15-003";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        // 候选：手牌中力量不高于 6000 的角色
        var candidates = me.Hand
            .Where(c => c.Info.Kind == Cards.CardKind.Character && c.Info.Power <= 6000)
            .ToList();
        if (candidates.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "爱比达：是否丢弃 1 张手牌（力量≤6000 的角色）以避免被 KO？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardHand",
            "选择 1 张要丢弃的角色卡",
            candidates.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;

        var toDiscard = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, toDiscard);

        // 标记此卡本次 KO 被置换（具体阻止 KO 需要 BattleEngine 配合 PreKO 钩子）
        ctx.Vars["PreventKO"] = true;
    }
}
