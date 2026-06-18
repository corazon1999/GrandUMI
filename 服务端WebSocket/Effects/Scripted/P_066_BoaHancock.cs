using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-066 波尔·汉库珂（角色 / 水 4 费 5000 / 王下七武海・九蛇海盗团）
/// 【我方的回合中】我方手牌不多于5张的场合，我方所有拥有《九蛇海盗团》特征的角色力量+1000。
///
/// 实现说明：
///   - 纯持续力量修正，用 ContinuousEffect.PowerDelta 注册（OnEnterField）。
///   - Scope 限我方角色；Filter 限《九蛇海盗团》特征。
///   - Predicate：仅我方回合中、且我方手牌 ≤5 张时生效。
///   - 离场由引擎自动清理；重复登场前先去重。
/// </summary>
public class P_066_BoaHancock : IScriptedEffect
{
    public string CardNumber => "P-066";

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
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Info.HasKeyword("九蛇海盗团"),
            },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                s.CurrentTurnPlayer == owner && s.Players[owner].Hand.Count <= 5,
        });
        return Task.CompletedTask;
    }
}
