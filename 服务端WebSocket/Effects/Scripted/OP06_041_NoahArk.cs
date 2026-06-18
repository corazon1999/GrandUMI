using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-041 方舟诺亚（舞台）
/// 【登场时】将对方所有角色转为休息状态。
/// 遍历对方角色逐张 RestCard。
/// </summary>
public class OP06_041_NoahArk : IScriptedEffect
{
    public string CardNumber => "OP06-041";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        foreach (var c in opp.Characters)
        {
            AtomicOps.RestCard(c);
        }
        return Task.CompletedTask;
    }
}
