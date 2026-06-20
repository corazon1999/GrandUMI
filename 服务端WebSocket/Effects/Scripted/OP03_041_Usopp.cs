using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-041 撒谎布（角色 / 水）
/// 【速攻】【咚!!×1】当通过此角色的攻击给予对方生命区伤害时，可以将我方卡组最上方的7张卡牌放置到废弃区。
///
/// 实现：监听 OnDamageToLeader。仅当攻击者为此角色、被赋予咚≥1 时，可选磨7张。【速攻】由引擎关键词处理。
/// </summary>
public class OP03_041_Usopp : IScriptedEffect
{
    public string CardNumber => "OP03-041";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDamageToLeader;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var attackerId = ctx.Vars.TryGetValue("attackerId", out var av) ? av as string : null;
        if (attackerId != self.Id.ToString()) return;
        if (me.AttachedDonCount(self.Id) < 1) return;                                   // 咚!!×1
        if (me.Deck.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "撒谎布：将我方卡组最上方7张卡牌放置到废弃区？");
        if (!use) return;
        AtomicOps.MillTop(me, 7);
    }
}
