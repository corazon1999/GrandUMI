using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-117 奈美（领航 / 水）
/// 规则上，我方只能将拥有《东海》特征的卡牌放入卡组，且我方卡组变为0张时我方胜利（非败北）。
/// 【咚!!×1】当通过此领袖的攻击给予对方生命区伤害时，可以将我方卡组最上方的1张卡牌放置到废弃区。
///
/// 实现：同 OP03-040 奈美。OnGameStart 登记 DeckOutVictoryPlayers；OnDamageToLeader 攻击者为此领袖且咚≥1 时可选磨1。
/// "只能放东海入卡组"为建卡限制，不在战斗引擎处理。
/// </summary>
public class P_117_Nami : IScriptedEffect
{
    public string CardNumber => "P-117";
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

        var attackerId = ctx.Vars.TryGetValue("attackerId", out var av) ? av as string : null;
        if (attackerId != self.Id.ToString()) return;
        if (me.AttachedDonCount(self.Id) < 1) return;
        if (me.Deck.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "奈美：将我方卡组最上方1张卡牌放置到废弃区？");
        if (!use) return;
        AtomicOps.MillTop(me, 1);
    }
}
