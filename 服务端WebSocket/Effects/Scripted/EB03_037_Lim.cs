using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-037 莉姆（角色 / 暗 / 时光旅诗，4 费 5000）
/// 【登场时】我方场上存在 7 张或更多咚!! 的场合，直到下个对方的结束阶段结束时为止，
///   我方所有拥有《时光旅诗》特征的领袖和角色力量 +1000。
///
/// 实现说明：
///   - 登场时若费用区咚!!总数 ≥7 → 注册一条群体持续力量 +1000(ContinuousEffect)。
///   - Scope 限我方(Side=0)，含领袖与角色，Filter 限《时光旅诗》特征。
///   - "直到下个对方结束阶段结束"跨回合：登场回合 baseTurn 注册，有效期延续到对方回合
///     (baseTurn+1)结束，Predicate 用 s.TurnCount <= baseTurn + 1 限定。
/// </summary>
public class EB03_037_Lim : IScriptedEffect
{
    public string CardNumber => "EB03-037";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        if (me.TotalDonInCostArea < 7) return Task.CompletedTask;

        int baseTurn = ctx.State.TurnCount;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = true,
                IncludeCharacters = true,
                Filter = c => c.Info.HasKeyword("时光旅诗"),
            },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                s.TurnCount <= baseTurn + 1,
        });

        return Task.CompletedTask;
    }
}
