using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-059 水蒸气杀人事件（事件）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +3000。
///   之后，丢弃我方的最多 2 张手牌。将我方卡组最上方的与丢弃张数相同数量的卡牌放置到废弃区。
///
/// 实现说明：
///   - +3000 为本次战斗修正（AddPowerThisBattle）。
///   - 丢弃张数由玩家选择（0~2），随后按实际丢弃张数从卡组顶等量废弃（MillTop）。
/// </summary>
public class OP09_059_SteamMurder : IScriptedEffect
{
    public string CardNumber => "OP09-059";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 本次战斗：我方最多 1 张领袖或角色 +3000
        var buffTargets = new List<CardInstance> { me.Leader };
        buffTargets.AddRange(me.Characters);
        var picked = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，选择最多 1 张领袖或角色力量 +3000",
            buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (picked.Count > 0)
        {
            var tgt = buffTargets.First(c => c.Id.ToString() == picked[0]);
            AtomicOps.AddPowerThisBattle(tgt, 3000);
        }

        // 之后：丢弃我方最多 2 张手牌
        var handCands = me.Hand.ToList();
        int maxDiscard = Math.Min(2, handCands.Count);
        int discarded = 0;
        if (maxDiscard > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "丢弃我方最多 2 张手牌",
                handCands.Select(c => c.Id.ToString()).ToList(), 0, maxDiscard);
            foreach (var id in chosen)
            {
                var card = handCands.First(c => c.Id.ToString() == id);
                AtomicOps.DiscardHand(me, card);
                discarded++;
            }
        }

        // 将卡组最上方与丢弃张数相同数量的卡牌放置到废弃区
        if (discarded > 0)
        {
            AtomicOps.MillTop(me, discarded);
        }
    }
}
