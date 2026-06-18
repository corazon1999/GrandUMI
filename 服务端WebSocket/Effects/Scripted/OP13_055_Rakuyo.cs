using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-055 拉克约（角色）
/// 【攻击时】我方手牌不多于 4 张的场合，本回合中，我方所有拥有的特征中包含
///   〈白胡子海盗团〉的角色力量 +1000。
///
/// 说明：
///   - 范围 buff，强制效果（无“可以”）。
///   - 仅作用于我方角色（不含领袖；文本为“所有……角色”）。
///   - 过滤条件：角色特征含〈白胡子海盗团〉。
/// </summary>
public class OP13_055_Rakuyo : IScriptedEffect
{
    public string CardNumber => "OP13-055";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 我方手牌不多于 4 张
        if (me.Hand.Count > 4) return Task.CompletedTask;

        // 本回合中，我方所有〈白胡子海盗团〉角色力量 +1000（不含领袖）
        AtomicOps.AddPowerToAllThisTurn(
            ctx.State, ctx.OwnerIndex,
            c => c.Info.HasKeyword("白胡子海盗团"),
            1000,
            includeLeader: false);

        return Task.CompletedTask;
    }
}
