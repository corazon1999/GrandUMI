using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-115 纸绘·残影（事件，光）
/// 【触发】抽取 1 张卡牌。
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +3000。
///         之后，对方生命卡牌不多于 2 张的场合，本回合中，我方最多 1 张领袖或角色力量 +1000。
///
/// 实现说明：
///   - 【触发】(OnLifeRevealTrigger) = 抽 1。
///   - 【反击】(EventCounter)：
///       1) 让玩家从我方领袖或角色中选最多 1 张，本次战斗 +3000；
///       2) 若对方生命牌 ≤2，再从我方领袖或角色中选最多 1 张，本回合 +1000（两次可选不同目标）。
///   - 第二段的条件判断（对方生命≤2）+ 第二次 Choose 无法用扁平的 DSL counter 数组表达，故用脚本。
/// </summary>
public class OP13_115_KamieZanei : IScriptedEffect
{
    public string CardNumber => "OP13-115";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ── 【触发】 ──
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        // ── 【反击】 ──
        if (ctx.Trigger != EffectTrigger.EventCounter) return;

        // 1) 我方最多 1 张领袖或角色，本次战斗 +3000
        var targets1 = new List<CardInstance> { me.Leader };
        targets1.AddRange(me.Characters);
        var chosen1 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择我方最多 1 张领袖或角色，本次战斗力量 +3000",
            targets1.Select(c => c.Id.ToString()).ToList(), 0, 1);
        foreach (var cid in chosen1)
        {
            var card = targets1.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.AddPowerThisBattle(card, 3000);
        }

        // 2) 之后：对方生命牌 ≤2 → 我方最多 1 张领袖或角色，本回合 +1000
        if (opp.LifeCount <= 2)
        {
            var targets2 = new List<CardInstance> { me.Leader };
            targets2.AddRange(me.Characters);
            var chosen2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "对方生命≤2：选择我方最多 1 张领袖或角色，本回合力量 +1000",
                targets2.Select(c => c.Id.ToString()).ToList(), 0, 1);
            foreach (var cid in chosen2)
            {
                var card = targets2.FirstOrDefault(c => c.Id.ToString() == cid);
                if (card is not null) AtomicOps.AddPowerThisTurn(card, 1000);
            }
        }
    }
}
