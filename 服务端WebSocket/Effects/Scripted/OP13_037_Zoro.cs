using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-037 罗罗诺亚·佐罗（4 费 5000，风，FILM/超新星/草帽一伙）
/// 【登场时】我方领袖拥有《FILM》或《草帽一伙》特征的场合，将我方最多 2 张咚!! 转为活跃状态。
/// 【我方的回合结束时】将此角色转为活跃状态。
///
/// 实现：
///   - OnEnterField：检查我方领袖是否含 FILM 或 草帽一伙 特征；满足则把最多 2 张休息咚转活跃
///     （咚之间无区别，自动取前 2 张，不需玩家选择）。
///   - OnMyTurnEnd：将此角色（ctx.Source）转为活跃状态（IsTapped=false）。
/// </summary>
public class OP13_037_Zoro : IScriptedEffect
{
    public string CardNumber => "OP13-037";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnMyTurnEnd;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            // 将此角色转为活跃状态
            ctx.Source.IsTapped = false;
            return Task.CompletedTask;
        }

        // OnEnterField：领袖须含 FILM 或 草帽一伙 特征
        var leader = me.Leader;
        bool ok = leader.Info.HasKeyword("FILM") || leader.Info.HasKeyword("草帽一伙");
        if (!ok) return Task.CompletedTask;

        // 将最多 2 张休息状态的咚!! 转为活跃状态
        int activated = 0;
        foreach (var d in me.CostArea)
        {
            if (activated >= 2) break;
            if (d.State == DonState.Rest)
            {
                d.State = DonState.Active;
                d.AttachedToCardId = null;
                activated++;
            }
        }

        return Task.CompletedTask;
    }
}
