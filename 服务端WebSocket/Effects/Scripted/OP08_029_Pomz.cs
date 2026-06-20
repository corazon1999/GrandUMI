using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-029 扁姆兹（角色 / 风 / 纯毛族·大妈海盗团）
/// 此角色处于活跃状态的场合，我方“扁姆兹”以外的费用不高于3且拥有《纯毛族》特征的角色，
/// 不会因效果而被KO。
///
/// 实现说明：
///   - 持续光环型「不会因效果被KO」→ ContinuousEffect.KoGuard="effect"（OnEnterField 注册）。
///   - Predicate：仅当来源（扁姆兹自身）处于活跃状态时生效；
///     受护对象为我方拥有《纯毛族》特征、当前费用≤3、且不是该来源扁姆兹本身的角色。
///   - 当前费用用 s.CurrentCostOf 评估（含持续费用修正）。
/// </summary>
public class OP08_029_Pomz : IScriptedEffect
{
    public string CardNumber => "OP08-029";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "effect",
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                !self.IsTapped &&                       // 来源扁姆兹处于活跃状态
                card.Id != selfId &&                    // “扁姆兹”以外
                card.Info.HasKeyword("纯毛族") &&
                s.CurrentCostOf(sideIdx, card) <= 3,
        });

        return Task.CompletedTask;
    }
}
