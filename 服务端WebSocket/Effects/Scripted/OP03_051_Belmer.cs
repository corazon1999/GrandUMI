using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-051 贝鲁梅尔（角色）
/// 【咚!!×1】当通过此角色的攻击给予对方生命区伤害时，可以将我方卡组最上方 7 张放置废弃区。
/// 【KO时】可以将我方卡组最上方 3 张卡牌放置到废弃区。
///
/// 实现说明：OnDamageToLeader 校验攻击者为本卡且附有咚×1，可选弃顶7张；保留【KO时】弃顶3张。
/// </summary>
public class OP03_051_Belmer : IScriptedEffect
{
    public string CardNumber => "OP03-051";

    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.OnKO or EffectTrigger.OnDamageToLeader;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Deck.Count == 0) return;

        if (ctx.Trigger == EffectTrigger.OnDamageToLeader)
        {
            var attackerId = ctx.Vars.TryGetValue("attackerId", out var av) ? av as string : null;
            if (attackerId != ctx.Source.Id.ToString() || me.AttachedDonCount(ctx.Source.Id) < 1) return;
            if (await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "贝鲁梅尔：将我方卡组最上方7张放置废弃区？"))
                AtomicOps.MillTop(me, 7);
            return;
        }

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "贝鲁梅尔【KO时】：是否将我方卡组最上方 3 张放置废弃区？");
        if (!use) return;

        AtomicOps.MillTop(me, 3);
    }
}
