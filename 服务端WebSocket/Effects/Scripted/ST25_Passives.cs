using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects.Dsl;

namespace GrandUMI.Effects.Scripted;

/// <summary>ST25 十字公会 持续被动集合（爱比达 / 卡巴奇 / 毛奇）。共同条件：我方场上≥2张原本费用≥5的角色。</summary>
internal static class St25Cond
{
    public static bool TwoHighCost(GameState s, int owner)
        => s.Players[owner].Characters.Count(c => c.Info.Cost >= 5) >= 2;
}

/// <summary>
/// ST25-001 爱比达：我方场上≥2张费用≥5角色时此角色费用+1（持续）。【登场时】领袖为“巴奇”时抽3丢2（委托 DSL）。
/// </summary>
public class ST25_001_Avido : IScriptedEffect
{
    public string CardNumber => "ST25-001";
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
            CostDelta = 1,
            Predicate = (s, sideIdx, card) => card.Id == selfId && St25Cond.TwoHighCost(s, owner),
        });
        await DslInterpreter.TryResolve(ctx);
    }
}

/// <summary>
/// ST25-002 卡巴奇：我方场上≥2张费用≥5角色时此角色获得【阻挡者】且费用+1；【对方的回合中】此角色力量+5000。（持续）
/// </summary>
public class ST25_002_Cabaji : IScriptedEffect
{
    public string CardNumber => "ST25-002";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, card) => card.Id == selfId && St25Cond.TwoHighCost(s, owner),
        });
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = 1,
            Predicate = (s, sideIdx, card) => card.Id == selfId && St25Cond.TwoHighCost(s, owner),
        });
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 5000,
            Predicate = (s, sideIdx, card) => card.Id == selfId && s.CurrentTurnPlayer != owner,
        });
        return Task.CompletedTask;
    }
}

/// <summary>
/// ST25-005 毛奇：我方场上≥2张费用≥5角色时此角色获得【阻挡者】且费用+1（持续）。【KO时】… 由 DSL ST25.json 实现。
/// </summary>
public class ST25_005_Mohji : IScriptedEffect
{
    public string CardNumber => "ST25-005";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, card) => card.Id == selfId && St25Cond.TwoHighCost(s, owner),
        });
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = 1,
            Predicate = (s, sideIdx, card) => card.Id == selfId && St25Cond.TwoHighCost(s, owner),
        });
        return Task.CompletedTask;
    }
}
