using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-043 爱德华·纽哥特（角色）
/// 【登场时】我方领袖拥有的特征中包含《白胡子海盗团》特征，且我方生命卡牌不多于 2 张的场合，
///   直到下个对方的回合结束时为止，对方所有角色将要攻击时，必须丢弃其 2 张手牌才可以攻击。
///
/// 实现说明：
/// - 用 Wave5 的攻击税 AttackTaxDiscard：对方所有角色攻击前须弃 2 张手牌，引擎在对方回合结束自动清。
/// - "直到下个对方回合结束" 由攻击税自动清除机制近似实现。
/// - 条件：领袖含《白胡子海盗团》特征 且 我方生命 ≤ 2。
/// </summary>
public class OP08_043_EdwardNewgate : IScriptedEffect
{
    public string CardNumber => "OP08-043";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：领袖含《白胡子海盗团》特征，且我方生命 ≤ 2
        if (!me.Leader.Info.HasKeyword("白胡子海盗团")) return Task.CompletedTask;
        if (me.LifeCount > 2) return Task.CompletedTask;

        // 对方所有角色攻击前须弃 2 张手牌
        ctx.State.AttackTaxDiscard[1 - ctx.OwnerIndex] = 2;

        return Task.CompletedTask;
    }
}
