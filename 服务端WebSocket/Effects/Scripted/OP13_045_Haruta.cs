using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-045 哈尔塔（角色）
/// 【攻击时】我方手牌不多于 4 张的场合，抽取 1 张卡牌。
/// </summary>
public class OP13_045_Haruta : IScriptedEffect
{
    public string CardNumber => "OP13-045";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        // 我方手牌不多于 4 张（注意：攻击时此卡已不在手牌中，统计场上手牌区）
        if (me.Hand.Count <= 4)
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        return Task.CompletedTask;
    }
}
