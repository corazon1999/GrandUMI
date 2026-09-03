using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects.Dsl;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST14-017 千里·阳光号（舞台）
/// 此舞台在场期间，我方场上的黑色《草帽一伙》角色费用+1。（持续）
/// 目标集合随当前场面动态计算，后续登场或重新登场的符合角色也会立即获得加成。
/// 【登场时】我方领袖拥有《草帽一伙》特征的场合，抽取1张卡牌。（委托 DSL ST14.json）
/// </summary>
public class ST14_017_SunnyGo : IScriptedEffect, IFieldStaticEffect
{
    public string CardNumber => "ST14-017";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task RegisterFieldStatic(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;

        // 同一来源重复注册只替换自身光环；目标资格始终按当前权威场面实时判定。
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            SourceCardNumber = CardNumber,
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = 1,
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                s.Players[owner].StageCards.Any(stage => stage.Id == selfId) &&
                s.Players[owner].Characters.Any(character => character.Id == card.Id) &&
                IsEligibleCharacter(card),
        });

        return Task.CompletedTask;
    }

    public async Task Resolve(EffectContext ctx)
    {
        // 登场时抽牌部分委托 DSL 执行
        await DslInterpreter.TryResolve(ctx);
    }

    private static bool IsEligibleCharacter(CardInstance card) =>
        card.Info.Kind == CardKind.Character &&
        card.Info.HasKeyword("草帽一伙") &&
        card.Info.ColorList.Contains("黑");
}
