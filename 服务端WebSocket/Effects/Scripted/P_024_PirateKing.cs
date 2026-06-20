using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-024 我一定会成为!!『海盗王』的!!!（事件 / 风 / 草帽一伙·超新星）
/// 【主要】本回合中，我方场上每存在 1 张角色，我方领袖力量 +1000。
/// 【触发】本回合中，我方最多 1 张领袖或角色力量 +1000。
///
/// 实现说明 / 简化点：
///   - 【主要】"每 1 张角色 +1000" 的总量随场上角色数动态变化，ContinuousEffect.PowerDelta 为
///     固定 int 无法表达动态量；故在事件结算瞬间按当前角色数快照一次性 AddPowerThisTurn。
///     （结算后若角色数变化不再重算，属合理简化。）
///   - 【触发】用 OnLifeRevealTrigger：选我方最多 1 张领袖/角色本回合 +1000。
/// </summary>
public class P_024_PirateKing : IScriptedEffect
{
    public string CardNumber => "P-024";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            // 本回合中，我方场上每存在 1 张角色，领袖力量 +1000（结算瞬间快照）
            int charCount = me.Characters.Count;
            if (charCount > 0)
                AtomicOps.AddPowerThisTurn(me.Leader, charCount * 1000);
            return;
        }

        // 【触发】本回合中，我方最多 1 张领袖或角色力量 +1000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择我方最多 1 张领袖或角色，本回合力量 +1000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, 1000);
        }
    }
}
