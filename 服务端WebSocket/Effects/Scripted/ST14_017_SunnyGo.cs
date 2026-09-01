using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects.Dsl;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST14-017 千里·阳光号（舞台）
/// 此舞台登场并建立静态效果时，我方场上已有的黑色《草帽一伙》角色费用+1。（持续）
/// 后续登场的角色不进入本次快照；已入选角色离场后，即使同一实例再次登场也不恢复加成。
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

        // 来源自身持有同一个 ID，表示本次留场已经建立过快照；重复结算只重建光环，不扩大目标集。
        // 舞台离场时 PlayerState 会清除此标记及所有目标标记，重登后才按新场面重新建立。
        if (!ctx.Source.FieldSnapshotSourceIds.Contains(selfId))
        {
            foreach (var character in ctx.State.Players.SelectMany(player => player.Characters))
                character.FieldSnapshotSourceIds.Remove(selfId);

            ctx.Source.FieldSnapshotSourceIds.Add(selfId);
            foreach (var character in ctx.State.Players[owner].Characters.Where(IsEligibleCharacter))
                character.FieldSnapshotSourceIds.Add(selfId);
        }

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
                card.FieldSnapshotSourceIds.Contains(selfId) &&
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
