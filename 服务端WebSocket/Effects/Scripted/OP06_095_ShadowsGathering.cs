using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-095 影子集合地（事件）
/// 【主要】/【反击】本回合中，我方的领袖力量+1000。之后，可以将我方任意张数的费用不高于 2
///   且拥有《恐怖之船海盗团》特征的角色 KO。每 KO 1 张角色，本回合中，我方的领袖力量+1000。
/// 【触发】抽取 2 张卡牌，丢弃我方的 1 张手牌。
///
/// 实现说明 / 简化点：
///   - 主体先 +1000，再让玩家多选任意张数符合条件的我方角色 KO，每 KO 1 张再 +1000。
///   - 【触发】抽 2 后让玩家自选弃 1 张手牌。
/// </summary>
public class OP06_095_ShadowsGathering : IScriptedEffect
{
    public string CardNumber => "OP06-095";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter
        || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】抽取 2 张卡牌，丢弃我方 1 张手牌
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
            if (me.Hand.Count > 0)
            {
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                    "丢弃我方 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
                if (chosen.Count > 0)
                {
                    var card = me.Hand.First(c => c.Id.ToString() == chosen[0]);
                    AtomicOps.DiscardHand(me, card);
                }
            }
            return;
        }

        // ── 【主要】/【反击】──
        // 本回合中我方领袖力量 +1000
        AtomicOps.AddPowerThisTurn(me.Leader, 1000);

        // 可以将任意张数费用≤2 且《恐怖之船海盗团》的我方角色 KO，每 KO 1 张领袖再 +1000
        var cands = me.Characters
            .Where(c => c.Info.Cost <= 2 && c.Info.HasKeyword("恐怖之船海盗团"))
            .ToList();
        if (cands.Count == 0) return;

        var chosenKo = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "可以将任意张数费用≤2 的《恐怖之船海盗团》我方角色 KO（每 KO 1 张领袖力量+1000）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, cands.Count);

        foreach (var id in chosenKo)
        {
            var tgt = cands.First(c => c.Id.ToString() == id);
            AtomicOps.KO(ctx.State, ctx.OwnerIndex, tgt);
            AtomicOps.AddPowerThisTurn(me.Leader, 1000);
        }
    }
}
