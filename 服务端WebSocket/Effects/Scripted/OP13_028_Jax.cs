using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-028 杰克斯（角色 / 风 10 费 12000，FILM/四皇/红发海盗团）
/// 【登场时】将我方所有的咚!!转为活跃状态。之后，本回合中，我方无法从手牌中打出卡牌。
///
/// 实现：
///   - 登场时把我方费用区内所有休息状态的咚!! 转为活跃状态（咚之间无区别，无需玩家选择）。
///
/// 简化点（引擎限制，已在 summary 注明）：
///   - "本回合中，我方无法从手牌中打出卡牌" 需要玩家级"出牌限制"引擎机制
///     （CanPlayCard / 出牌入口按本回合标记拦截一切手牌打出），当前引擎无此能力，故省略。
///     与现有 OP12-030 米霍克的处理一致：实现 ramp 核心收益，注明出牌限制为简化点。
/// </summary>
public class OP13_028_Jax : IScriptedEffect
{
    public string CardNumber => "OP13-028";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 将我方所有休息状态的咚!! 转为活跃状态
        foreach (var d in me.CostArea)
        {
            if (d.State == DonState.Rest)
            {
                d.State = DonState.Active;
                d.AttachedToCardId = null;
            }
        }

        return Task.CompletedTask;
    }
}
