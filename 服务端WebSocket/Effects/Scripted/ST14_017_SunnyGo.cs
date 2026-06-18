using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects.Dsl;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST14-017 千里·阳光号（舞台）
/// 我方所有拥有《草帽一伙》特征的黑色角色费用+1。（持续）
/// 【登场时】我方领袖拥有《草帽一伙》特征的场合，抽取1张卡牌。（委托 DSL ST14.json）
/// </summary>
public class ST14_017_SunnyGo : IScriptedEffect
{
    public string CardNumber => "ST14-017";
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
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                card.Id != s.Players[owner].Leader.Id &&
                card.Info.HasKeyword("草帽一伙") &&
                card.Info.ColorList.Contains("紫"),
        });
        // 登场时抽牌部分委托 DSL 执行
        await DslInterpreter.TryResolve(ctx);
    }
}
