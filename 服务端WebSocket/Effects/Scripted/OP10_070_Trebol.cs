using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-070 托雷波尔（角色）
/// 【阻挡者】（关键词，由引擎处理）
/// 【登场时】直到下个对方的回合结束时为止，我方所有原本的力量不高于1000的角色
///   不会因对方的效果而被KO。
///
/// 说明 / 简化点：
///   - 用持续 KoGuard="effect"（仅因效果被KO）+ Predicate 实现群体防KO。
///   - 有效期"直到下个对方回合结束"：在登场时记录基准回合数 baseTurn，
///     Predicate 用 s.TurnCount <= baseTurn + 1 限制（覆盖到下个对方回合）。
///   - "原本的力量不高于1000"取卡面原始力量 c.Info.Power。
/// </summary>
public class OP10_070_Trebol : IScriptedEffect
{
    public string CardNumber => "OP10-070";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;
        int baseTurn = ctx.State.TurnCount;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "effect",
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                card.Info.Power <= 1000 &&
                s.TurnCount <= baseTurn + 1,
        });

        return Task.CompletedTask;
    }
}
