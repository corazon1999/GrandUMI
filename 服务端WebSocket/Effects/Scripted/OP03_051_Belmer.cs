using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-051 贝鲁梅尔（角色）
/// 【咚!!×1】当通过此角色的攻击给予对方生命区伤害时，可以将我方卡组最上方 7 张放置废弃区。
///   —— 引擎无"给予生命伤害时"触发钩子(OnDealDamage)，此部分未实现。
/// 【KO时】可以将我方卡组最上方 3 张卡牌放置到废弃区。
///
/// 实现说明：仅实现【KO时】弃顶 3（可选）。
/// </summary>
public class OP03_051_Belmer : IScriptedEffect
{
    public string CardNumber => "OP03-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Deck.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "贝鲁梅尔【KO时】：是否将我方卡组最上方 3 张放置废弃区？");
        if (!use) return;

        AtomicOps.MillTop(me, 3);
    }
}
