using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-030 杰拉基尔·米霍克（8 费 8000）
/// 【阻挡者】（静态关键字，由卡牌数据驱动，无需脚本实现）
/// 【登场时】将我方最多 4 张咚!! 转为活跃状态。
///   之后，本回合中，我方无法登场原本的费用为 7 或更高的角色卡牌。
///
/// 实现：登场时把我方最多 4 张休息状态的咚!! 转为活跃状态（自动取前 4 张，不需玩家选择，
/// 因为咚!! 之间无区别）。
///
/// 简化点（未实现）：
///   - "本回合中我方无法登场原本费用 7+ 的角色" 需要玩家级"出牌限制"引擎机制
///     （CanPlayCard 时按本回合标记拦截费用 ≥7 的角色），当前引擎无此能力，故省略。
///     该限制不影响本卡 ramp 的核心收益，已在 summary 注明。
/// </summary>
public class OP12_030_Mihawk : IScriptedEffect
{
    public string CardNumber => "OP12-030";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 将最多 4 张休息状态的咚!! 转为活跃状态
        int activated = 0;
        foreach (var d in me.CostArea)
        {
            if (activated >= 4) break;
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
