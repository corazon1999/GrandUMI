using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-040 奈美（领航 / 水）
/// 规则上，我方卡组变为0张的场合，我方不会败北而是会胜利。
/// 【咚‼×1】当通过此领袖的攻击给予对方生命区伤害时，可以将我方卡组最上方的1张卡牌放置到废弃区。
///
/// 实现：
///   - OnGameStart：登记 DeckOutVictoryPlayers（TurnEngine.DrawCard 卡组0张时改判此玩家胜利）。
///   - OnDamageToLeader：仅当攻击者为此领袖、且被赋予咚≥1 时，可选将卡组顶1张磨入废弃。
/// </summary>
public class OP03_040_Nami : IScriptedEffect
{
    public string CardNumber => "OP03-040";
    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnGameStart || t == EffectTrigger.OnDamageToLeader;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnGameStart)
        {
            ctx.State.DeckOutVictoryPlayers.Add(ctx.OwnerIndex);
            return;
        }

        // OnDamageToLeader：通过此领袖的攻击给予对方生命伤害
        var attackerId = ctx.Vars.TryGetValue("attackerId", out var av) ? av as string : null;
        if (attackerId != self.Id.ToString()) return;
        if (me.AttachedDonCount(self.Id) < 1) return;                                   // 咚‼×1
        if (me.Deck.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "奈美：将我方卡组最上方1张卡牌放置到废弃区？");
        if (!use) return;
        AtomicOps.MillTop(me, 1);
    }
}
