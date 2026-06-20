using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-023 「虽然是个傻儿子，我也还是爱你啊……」（事件 / 炎）
/// 【主要】我方生命卡牌不多于3张的场合，本回合中，我方无法通过我方的效果将生命卡牌加入手牌。
/// 【触发】本回合中，我方最多1张领袖力量+1000。
///
/// 实现：EventMain（生命≤3 时置 NoEffectLifeToHandThisTurn）+ OnLifeRevealTrigger（我方领袖+1000）。
/// </summary>
public class OP02_023_StupidSon : IScriptedEffect
{
    public string CardNumber => "OP02-023";
    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            if (me.LifeArea.Count <= 3)
                ctx.State.NoEffectLifeToHandThisTurn.Add(ctx.OwnerIndex);
            return Task.CompletedTask;
        }

        // OnLifeRevealTrigger：我方最多1张领袖力量+1000（仅领袖，直接应用）
        AtomicOps.AddPowerThisTurn(me.Leader, 1000);
        return Task.CompletedTask;
    }
}
