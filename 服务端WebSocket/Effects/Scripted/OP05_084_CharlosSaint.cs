using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-084 恰尔罗斯圣（角色）
/// 持续：【我方的回合中】我方场上的角色仅为拥有《天龙人》特征的角色的场合，
///        对方所有角色费用 -4。
///
/// 实现：用 ContinuousEffect.CostDelta 注册（OnEnterField 时机），
///   Scope.Side = 1（作用于对方），仅作用于角色（不含领袖）。
///   Predicate：在我方回合中，且我方场上全部角色都拥有《天龙人》特征时生效。
///   （费用持续修正影响 KO/选择判定与卡面显示，由引擎在 ContinuousCostBonus 中累加。）
/// </summary>
public class OP05_084_CharlosSaint : IScriptedEffect
{
    public string CardNumber => "OP05-084";

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
            CostDelta = -4,
            Predicate = (s, sideIdx, c) =>
                sideIdx == 1 - owner                                   // 作用于对方角色
                && c.Id != s.Players[1 - owner].Leader.Id              // 排除对方领袖
                && s.CurrentTurnPlayer == owner                        // 我方回合中
                && s.Players[owner].Characters.Count > 0               // 我方有角色
                && s.Players[owner].Characters.All(ch => ch.Info.HasKeyword("天龙人")),
        });

        return Task.CompletedTask;
    }
}
