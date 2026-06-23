using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects.Dsl;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-011 帕鲁（角色 / 东海）
/// 【对方的回合中】我方领袖拥有《东海》特征的场合，此角色获得【阻挡者】效果，且力量+2000。
/// 【KO时】… 由 DSL OP15_wf.json 实现（本脚本不接管 OnKO，退回 DSL）。
///
/// 本脚本：登场时注册条件持续 ContinuousEffect —— 对方回合且我方领袖含《东海》时，
///   此角色获得【阻挡者】并力量+2000（同一条件，单个 effect 承载 GrantKeyword + PowerDelta）。
/// </summary>
public class OP15_011_Pearl : IScriptedEffect
{
    public string CardNumber => "OP15-011";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer != owner &&
                s.Players[owner].Leader.Info.HasKeyword("东海"),
        });
        await DslInterpreter.TryResolve(ctx);
    }
}
