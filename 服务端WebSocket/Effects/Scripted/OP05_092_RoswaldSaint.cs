using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-092 罗兹瓦德圣（角色）
/// 持续：【我方的回合中】我方场上的角色仅为拥有《天龙人》特征的角色的场合，
///        对方所有角色费用 -6。
///
/// 实现：与 OP05-084 同构，CostDelta = -6。
///   用 ContinuousEffect.CostDelta 注册（OnEnterField 时机），Scope.Side = 1（对方角色）。
///   Predicate：我方回合中、且我方场上全部角色都拥有《天龙人》特征时生效。
/// </summary>
public class OP05_092_RoswaldSaint : IScriptedEffect
{
    public string CardNumber => "OP05-092";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        var selfId = ctx.Source.Id;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = -6,
            Predicate = (s, sideIdx, c) =>
                sideIdx == 1 - owner
                && c.Id != s.Players[1 - owner].Leader.Id
                && s.CurrentTurnPlayer == owner
                && s.Players[owner].Characters.Count > 0
                && s.Players[owner].Characters.All(ch => ch.Info.HasKeyword("天龙人")),
        });

        return Task.CompletedTask;
    }
}
