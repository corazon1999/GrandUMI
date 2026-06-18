using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-096 人的梦想!!是不会终止的!!（事件）
/// 【反击】将我方卡组最上方的 1 张卡牌放置到废弃区。放置的卡牌费用为 6 或更高的场合，
///   本次战斗中，我方最多 1 张领袖或角色力量 +5000。
///
/// 实现说明 / 简化点：
///   - DSL 无法表达"刚废弃的卡组顶牌费用 ≥6 再连锁"，故脚本实现：先取卡组顶牌（在放弃前
///     记录其卡面费用），手动移入废弃区，再据费用判断是否提供 +5000。
///   - 费用判定取卡面原始费用 c.Info.Cost（卡组牌无场上费用修正，与文本一致）。
/// </summary>
public class OP08_096_HumansDreamsNeverEnd : IScriptedEffect
{
    public string CardNumber => "OP08-096";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 将卡组最上方 1 张放置到废弃区
        if (me.Deck.Count == 0) return;
        var top = me.Deck[0];
        int milledCost = top.Info.Cost;
        me.Deck.RemoveAt(0);
        me.Trash.Add(top);

        // 费用 ≥6 时，本次战斗中我方最多 1 张领袖或角色 +5000
        if (milledCost < 6) return;

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，选择我方最多 1 张领袖或角色 +5000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 5000);
        }
    }
}
