using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-020 铁桶王国（舞台）
/// 【对方的回合中】我方所有拥有《铁桶王国》特征的角色力量 +1000。
///
/// 实现说明：
///   - 纯持续力量修正，用 ContinuousEffect 在登场时注册。
///   - Scope：源卡同方(Side=0)，仅角色，过滤拥有《铁桶王国》特征。
///   - Predicate：仅在对方回合中生效。
///   - 舞台离场时由引擎自动清理；重复注册前先去重。
/// </summary>
public class OP08_020_DrumKingdom : IScriptedEffect
{
    public string CardNumber => "OP08-020";

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
                Filter = c => c.Info.HasKeyword("铁桶王国"),
            },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) => s.CurrentTurnPlayer != owner,
        });

        return Task.CompletedTask;
    }
}
