using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-003 爱比达（置换 KO 效果）
/// 此角色将要被 KO 的场合，可以改为丢弃我方手牌中 1 张力量不高于 6000 的角色卡牌，使此角色不会被 KO。
///
/// 通过 PreKO 触发实现：BattleEngine.KOCardAsync 在 KO 卡牌前先 Resolve PreKO，
/// 此脚本写入 ctx.State.MarkPreventKO(ctx.Source.Id) 即可取消本次 KO。
/// </summary>
public class OP15_003_Apis : IScriptedEffect
{
    public string CardNumber => "OP15-003";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.PreKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
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
        ctx.State.MarkPreventKO(ctx.Source.Id);
    }
}
