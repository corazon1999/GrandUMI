using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-051 杰克斯（角色）
/// 【攻击时】可以丢弃我方任意张数的手牌。每丢弃 1 张，本次战斗中，此角色的力量 +1000。
///
/// 实现说明：
/// - 用 ChooseCards(0..手牌数) 让玩家一次性选出要丢弃的任意张数手牌。
/// - 逐张 DiscardHand，并按张数 AddPowerThisBattle(+1000/张)。
/// </summary>
public class P_051_Shanks : IScriptedEffect
{
    public string CardNumber => "P-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        var me = ctx.State.Players[owner];
        var self = ctx.Source;

        var hand = me.Hand.ToList();
        if (hand.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(owner, "OwnHand",
            "丢弃任意张数手牌，每张本次战斗此角色力量 +1000",
            hand.Select(c => c.Id.ToString()).ToList(), 0, hand.Count);
        if (chosen.Count == 0) return;

        foreach (var id in chosen)
        {
            var card = hand.First(c => c.Id.ToString() == id);
            AtomicOps.DiscardHand(me, card);
        }
        AtomicOps.AddPowerThisBattle(self, 1000 * chosen.Count);
    }
}
