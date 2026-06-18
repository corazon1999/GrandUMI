using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-102 甚平（角色）
/// 【触发】将对方最多1张费用不高于4的角色放回其持有者的手牌，并将此卡牌加入手牌。
///
/// 实现说明：
///   - 【触发】(OnLifeRevealTrigger)：触发时此卡已被放入废弃区。
///   - 先让玩家选择对方最多 1 张费用≤4 的角色退回手牌（BounceToHand）。
///   - 之后将此卡（在废弃区）加入手牌（TrashToHand）。
/// </summary>
public class OP07_102_Jinbe : IScriptedEffect
{
    public string CardNumber => "OP07-102";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 将对方最多 1 张费用≤4 的角色退回手牌
        var cands = opp.Characters.Where(c => c.Info.Cost <= 4).ToList();
        if (cands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张费用≤4 的角色退回手牌",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.BounceToHand(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }

        // 将此卡（在废弃区）加入手牌
        if (me.Trash.Contains(ctx.Source))
            AtomicOps.TrashToHand(me, ctx.Source);
    }
}
