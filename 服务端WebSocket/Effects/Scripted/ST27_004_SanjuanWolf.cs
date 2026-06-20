using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects.Dsl;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST27-004 圣胡安·恶狼（角色）
/// 我方领袖拥有《黑胡子海盗团》特征的场合，此角色获得【阻挡者】效果（持续）。
/// 原文另有“我方废弃区每4张卡牌此角色费用+1”，因 ContinuousEffect.CostDelta 为定值无法随废弃张数动态变化，暂略去。
/// 【登场时】丢弃我方1张手牌（委托 DSL ST27.json）。
/// </summary>
public class ST27_004_SanjuanWolf : IScriptedEffect
{
    public string CardNumber => "ST27-004";
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
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].Leader.Info.HasKeyword("黑胡子海盗团"),
        });
        await DslInterpreter.TryResolve(ctx);
    }
}
