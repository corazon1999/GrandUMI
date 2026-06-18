using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-046 Miss.双指(萨拉)（角色 / 地 / 巴洛克工作室 cost4 power5000）
/// 【登场时】场上存在费用为 0 或费用为 8 或更高的角色的场合，抽取 1 张卡牌。
/// 【KO时】将我方卡组最上方的 2 张卡牌放置到废弃区。
///
/// 实现说明：
///   - 登场段条件：双方场上任一角色的当前费用为 0 或 ≥8（用 CurrentCostOf 评估，含持续费用修正）。
///   - KO 段：MillTop(me, 2) 将卡组顶 2 张放入废弃区。
/// </summary>
public class EB03_046_Sarah : IScriptedEffect
{
    public string CardNumber => "EB03-046";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int owner = ctx.OwnerIndex;
        int oppIdx = 1 - owner;
        var opp = ctx.State.Players[oppIdx];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            bool Cond(int sideIdx, CardInstance c)
            {
                int cost = ctx.State.CurrentCostOf(sideIdx, c);
                return cost == 0 || cost >= 8;
            }
            bool exists =
                me.Characters.Any(c => Cond(owner, c)) ||
                opp.Characters.Any(c => Cond(oppIdx, c));
            if (exists)
                AtomicOps.Draw(ctx.State, owner, 1);
            return Task.CompletedTask;
        }

        // 【KO时】卡组顶 2 张放置到废弃区
        AtomicOps.MillTop(me, 2);
        return Task.CompletedTask;
    }
}
