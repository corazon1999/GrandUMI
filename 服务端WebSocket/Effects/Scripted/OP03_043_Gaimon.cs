using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-043 盖蒙（角色 / 水 / 0力）
/// 当给予对方生命区伤害时，可以将我方卡组最上方的3张卡牌放置到废弃区。那样做的场合，此角色放置到废弃区。
///
/// 实现：监听 OnDamageToLeader（payload defenderOwner=受伤方）。文本未限定"通过此角色"，故只要我方对
/// 对方生命造成伤害即可触发。可选磨3张；若执行，则此角色放入废弃区（归还附着咚）。
/// </summary>
public class OP03_043_Gaimon : IScriptedEffect
{
    public string CardNumber => "OP03-043";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDamageToLeader;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var defenderOwner = ctx.Vars.TryGetValue("defenderOwner", out var dv) && dv is int di ? di : -1;
        if (defenderOwner != 1 - ctx.OwnerIndex) return;                                // 须为对方生命受伤
        if (me.Deck.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "盖蒙：将我方卡组最上方3张放置废弃区？（执行则此角色也放入废弃区）");
        if (!use) return;

        AtomicOps.MillTop(me, 3);

        // 此角色放入废弃区：归还附着咚后移入废弃
        foreach (var d in me.CostArea)
            if (d.State == DonState.Attached && d.AttachedToCardId == self.Id)
            { d.State = DonState.Rest; d.AttachedToCardId = null; }
        if (me.Characters.Remove(self)) me.Trash.Add(self);
    }
}
