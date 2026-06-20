using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-080 小熊玩具（事件 / 堂吉诃德海盗团）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +4000。
///   之后，我方场上的咚!!为 7 张或更多，且我方手牌不多于 5 张的场合，抽取 1 张卡牌。
/// 【触发】从咚!!卡组中追加最多 1 张活跃状态的咚!!。（关键词触发由引擎处理，此处实现反击主体）
///
/// 实现说明 / 简化点：
///   - "我方场上咚!!为 7 张或更多"= me.TotalDonInCostArea &gt;= 7。
///   - "手牌不多于 5 张"在加力量结算后判定（此时本事件已离手）。
/// </summary>
public class OP10_080_BearToy : IScriptedEffect
{
    public string CardNumber => "OP10-080";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 本次战斗中，我方最多 1 张领袖或角色 +4000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张领袖或角色，本次战斗力量 +4000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 4000);
        }

        // 之后：我方场上咚!! ≥ 7 且手牌 ≤ 5 → 抽 1
        if (me.TotalDonInCostArea >= 7 && me.Hand.Count <= 5)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        }
    }
}
