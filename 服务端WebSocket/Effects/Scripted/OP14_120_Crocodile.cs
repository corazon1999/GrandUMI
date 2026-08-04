using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-120 克洛克达尔（角色）
/// 【KO时】可以丢弃我方的 1 张手牌：从废弃区中登场此角色卡牌。
/// </summary>
public class OP14_120_Crocodile : IScriptedEffect
{
    public string CardNumber => "OP14-120";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【KO时】结算时，本卡必须仍在我方废弃区。
        if (!me.Trash.Contains(ctx.Source) || me.Hand.Count == 0) return;

        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "克洛克达尔【KO时】：丢弃我方 1 张手牌，从废弃区登场此角色？"))
            return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方 1 张手牌",
            me.Hand.Select(card => card.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count < 1) return;

        var discard = me.Hand.FirstOrDefault(card => card.Id.ToString() == chosen[0]);
        if (discard is null) return;

        AtomicOps.DiscardHand(me, discard);
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
    }
}
