using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-078 艾狄欧（角色 / 风 / 时光旅诗）
/// 我方场上存在 2 张或更多处于休息状态且拥有《时光旅诗》特征的角色的场合，此角色的力量 +1000。
///
/// 实现说明：纯持续条件力量，用 ContinuousEffect.PowerDelta + Predicate（OnEnterField 注册）。
/// </summary>
public class P_078_Idio : IScriptedEffect
{
    public string CardNumber => "P-078";

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
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true, Filter = c => c.Id == selfId },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                s.Players[owner].Characters.Count(c => c.IsTapped && c.Info.HasKeyword("时光旅诗")) >= 2,
        });
        return Task.CompletedTask;
    }
}
