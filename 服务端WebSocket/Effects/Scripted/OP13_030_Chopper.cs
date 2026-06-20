using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-030 托尼托尼·乔巴（风 / 动物·FILM·草帽一伙）
/// 【登场时】将我方最多 2 张咚!!转为活跃状态。
///
/// 说明：
///   - "将休息状态的咚转为活跃状态"，DSL 无对应 op，故用脚本直接操作 CostArea。
///   - 强制效果（无"可以"），尽量转 2 张休息咚；不足则尽量转。
/// </summary>
public class OP13_030_Chopper : IScriptedEffect
{
    public string CardNumber => "OP13-030";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int converted = 0;
        foreach (var d in me.CostArea)
        {
            if (converted >= 2) break;
            if (d.State == DonState.Rest)
            {
                d.State = DonState.Active;
                converted++;
            }
        }

        return Task.CompletedTask;
    }
}
