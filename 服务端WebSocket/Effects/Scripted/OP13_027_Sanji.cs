using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-027 山智（角色 / 风 5 费 7000，FILM/草帽一伙）
/// 【登场时】将我方最多2张咚!!转为活跃状态。
/// 【我方的回合结束时】我方领袖拥有《FILM》或《草帽一伙》特征的场合，
///   将我方最多1张咚!!转为活跃状态。
///
/// 实现：
///   - 两个触发：OnEnterField（登场时）+ OnMyTurnEnd（我方的回合结束时）。
///   - "最多N张"咚转活跃：把休息状态的咚!! 取前 N 张转为活跃（咚之间无区别，无需玩家选择）。
///   - 回合结束时附带领袖特征条件（FILM 或 草帽一伙）。
/// </summary>
public class OP13_027_Sanji : IScriptedEffect
{
    public string CardNumber => "OP13-027";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnMyTurnEnd;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            ActivateRestDon(me, 2);
        }
        else if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            // 我方领袖拥有《FILM》或《草帽一伙》特征
            if (me.Leader.Info.HasKeyword("FILM") || me.Leader.Info.HasKeyword("草帽一伙"))
                ActivateRestDon(me, 1);
        }

        return Task.CompletedTask;
    }

    // 将我方最多 max 张休息状态的咚!! 转为活跃状态
    private static void ActivateRestDon(PlayerState me, int max)
    {
        int activated = 0;
        foreach (var d in me.CostArea)
        {
            if (activated >= max) break;
            if (d.State == DonState.Rest)
            {
                d.State = DonState.Active;
                d.AttachedToCardId = null;
                activated++;
            }
        }
    }
}
