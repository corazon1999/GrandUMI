using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-100 大王椰树（角色 / 水）
/// 【触发】可以将我方生命区最上方或最下方的1张卡牌放置到废弃区：此卡牌登场。
///
/// 实现：OnLifeRevealTrigger，可选支付成本（将我方生命顶或底1张放入废弃区）后，将此卡从废弃区登场。
/// </summary>
public class OP03_100_RoyalPalm : IScriptedEffect
{
    public string CardNumber => "OP03-100";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "大王椰树：将我方生命顶或底1张放入废弃区，使此卡登场？");
        if (!use) return;

        int which = me.LifeArea.Count == 1 ? 0
            : await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择放入废弃区的生命牌",
                new List<string> { "生命区最上方", "生命区最下方" });
        var life = which == 0 ? me.LifeArea[0] : me.LifeArea[^1];
        me.LifeArea.Remove(life);
        me.Trash.Add(life);

        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
    }
}
