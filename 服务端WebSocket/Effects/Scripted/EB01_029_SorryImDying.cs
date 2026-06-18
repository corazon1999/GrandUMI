using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-029 抱歉！！我要死了！！（事件 / 水 / 东海·草帽一伙）
/// 【反击】公开我方卡组最上方的 1 张卡牌，且公开的卡牌为费用为 4 或更高的场合，
///   将我方最多 1 张角色放回其持有者的手牌。之后，将公开的卡牌放回卡组最下方。
///
/// 实现说明：
///   - 【反击】= EventCounter 时机。
///   - 公开卡组顶 1 张并向对手广播牌面；按其费用 ≥4 做条件分支。
///   - 满足时：将我方最多 1 张角色放回手牌（BounceToHand）。
///   - 之后：将公开的那张卡放回卡组最下方。
/// </summary>
public class EB01_029_SorryImDying : IScriptedEffect
{
    public string CardNumber => "EB01-029";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Deck.Count == 0) return;

        // 公开卡组最上方 1 张
        var revealed = me.Deck[0];
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new List<string> { revealed.Info.Number });

        // 条件：公开的卡牌费用 ≥4
        if (revealed.Info.Cost >= 4)
        {
            var cands = me.Characters.ToList();
            if (cands.Count > 0)
            {
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                    "将我方最多 1 张角色放回手牌",
                    cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
                if (chosen.Count > 0)
                {
                    var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                    AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, tgt);
                }
            }
        }

        // 之后：将公开的卡牌放回卡组最下方
        if (me.Deck.Contains(revealed))
        {
            me.Deck.Remove(revealed);
            me.Deck.Add(revealed);
        }
    }
}
