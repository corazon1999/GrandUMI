using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-034 布鲁克（角色 / 风 / 3 费 / 4000 / FILM・草帽一伙）
/// 【登场时】我方领袖拥有《FILM》或《草帽一伙》特征的场合，将我方最多 1 张咚!!转为活跃状态。
///
/// 实现：登场时若领袖含〈FILM〉或〈草帽一伙〉特征，则把我方 1 张休息状态的咚!!转为活跃状态。
///   "最多 1 张"为非强制，但咚!!之间无区别且这是纯收益，自动转活跃 1 张（无休息咚则跳过）。
/// </summary>
public class OP13_034_Brook : IScriptedEffect
{
    public string CardNumber => "OP13-034";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方领袖含〈FILM〉或〈草帽一伙〉特征
        bool ok = me.Leader.Info.HasKeyword("FILM") || me.Leader.Info.HasKeyword("草帽一伙");
        if (!ok) return Task.CompletedTask;

        // 将最多 1 张休息状态的咚!!转为活跃状态
        var don = me.CostArea.FirstOrDefault(d => d.State == DonState.Rest);
        if (don is not null)
        {
            don.State = DonState.Active;
            don.AttachedToCardId = null;
        }

        return Task.CompletedTask;
    }
}
