using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-025 可比（角色 / 风 5 费 6000，FILM/海军）
/// 【阻挡者】（由引擎按效果文本自动识别，无需脚本处理）
/// 【登场时】我方领袖拥有《FILM》特征或属性（打）的场合，将我方最多1张咚!!转为活跃状态。
///
/// 实现：
///   - 仅实现【登场时】：领袖满足「拥有《FILM》特征 或 属性为打」二者其一即可
///     （DSL 的 if 为 AND 语义，无法表达 OR，故用脚本）。
///   - 满足条件时把最多 1 张休息状态的咚!! 转为活跃。
///   - 【阻挡者】由 ActionValidator.HasKeyword（读效果文本含【阻挡者】）自动生效。
/// </summary>
public class OP13_025_Koby : IScriptedEffect
{
    public string CardNumber => "OP13-025";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 我方领袖拥有《FILM》特征 或 属性（打）
        bool ok = me.Leader.Info.HasKeyword("FILM") || me.Leader.Info.Property == "打";
        if (!ok) return Task.CompletedTask;

        // 将我方最多 1 张休息状态的咚!! 转为活跃状态
        foreach (var d in me.CostArea)
        {
            if (d.State == DonState.Rest)
            {
                d.State = DonState.Active;
                d.AttachedToCardId = null;
                break;
            }
        }

        return Task.CompletedTask;
    }
}
