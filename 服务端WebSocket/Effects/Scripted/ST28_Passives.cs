using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects.Dsl;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST28-002 伊佐：【咚!!×2】此角色获得【阻挡者】（持续）。【登场时】我方和之国领袖获得【流放】（委托 DSL）。
/// </summary>
public class ST28_002_Izo : IScriptedEffect
{
    public string CardNumber => "ST28-002";
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
            Predicate = (s, sideIdx, card) => card.Id == selfId && s.Players[owner].AttachedDonCount(selfId) >= 2,
        });
        await DslInterpreter.TryResolve(ctx);
    }
}

/// <summary>
/// ST28-004 光月桃之助：【我方的回合中】我方生命≤2 时我方领袖力量+1000（持续）。【启动主要】… 由 DSL ST28.json 实现。
/// </summary>
public class ST28_004_Momonosuke : IScriptedEffect
{
    public string CardNumber => "ST28-004";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                card.Id == s.Players[owner].Leader.Id &&
                s.CurrentTurnPlayer == owner &&
                s.Players[owner].LifeCount <= 2,
        });
        return Task.CompletedTask;
    }
}

/// <summary>
/// ST28-005 大和：【咚!!×2】【我方的回合中】此角色力量+3000（持续）。【登场时】检索《和之国》（委托 DSL）。
/// </summary>
public class ST28_005_Yamato : IScriptedEffect
{
    public string CardNumber => "ST28-005";
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
            PowerDelta = 3000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 2 &&
                s.CurrentTurnPlayer == owner,
        });
        await DslInterpreter.TryResolve(ctx);
    }
}
