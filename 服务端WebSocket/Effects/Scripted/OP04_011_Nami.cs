using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-011 奈美（角色 / 炎 / 阿拉巴斯坦王国・草帽一伙）
/// 【攻击时】公开我方卡组最上方的 1 张卡牌，且公开的卡牌为力量为 6000 或更高的
///   角色卡牌的场合，本回合中，此角色的力量+3000。之后，将公开的卡牌放回卡组最下方。
///
/// 实现说明：
///   - 看卡组顶 1 张并向对手公开（BroadcastReveal），据其原始力量判定：
///     角色卡且 Info.Power >= 6000 时自身本回合 +3000。
///   - 之后将该卡放回卡组最下方。
/// </summary>
public class OP04_011_Nami : IScriptedEffect
{
    public string CardNumber => "OP04-011";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.Deck.Count == 0) return Task.CompletedTask;

        var topCard = me.Deck[0];

        // 向对手公开顶 1 张
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new List<string> { topCard.Info.Number });

        // 条件：力量为 6000 或更高的角色卡牌
        if (topCard.Info.Kind == CardKind.Character && topCard.Info.Power >= 6000)
        {
            AtomicOps.AddPowerThisTurn(self, 3000);
        }

        // 之后：将公开的卡牌放回卡组最下方
        me.Deck.Remove(topCard);
        me.Deck.Add(topCard);

        return Task.CompletedTask;
    }
}
