using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-097 圣地玛丽乔尔（舞台）
/// 【我方的回合中】我方从手牌中登场的费用为2或更高且拥有《天龙人》特征的角色卡牌需支付的费用减少1。
///
/// 实现说明：
///   - 持续手牌打出费用减免，用 ContinuousEffect.CostDelta = -1 注册（OnEnterField 时机）。
///   - Scope.Side = 0（我方），仅作用于角色卡。
///   - Predicate：仅【我方的回合中】生效；目标卡费用≥2 且拥有《天龙人》特征。
///     引擎打出手牌时按 HandPlayCost 应用 CostDelta（sideIdx = 打出方）。
/// </summary>
public class OP05_097_MaryGeoise : IScriptedEffect
{
    public string CardNumber => "OP05-097";

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
            CostDelta = -1,
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner                         // 我方打出
                && s.CurrentTurnPlayer == owner          // 【我方的回合中】
                && c.Info.Kind == CardKind.Character
                && c.Info.Cost >= 2
                && c.Info.HasKeyword("天龙人"),
        });

        return Task.CompletedTask;
    }
}
