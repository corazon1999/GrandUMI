using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-012 奈菲特·可不拉（角色 / 炎 / 阿拉巴斯坦王国）
/// 【我方的回合中】除此角色以外我方其他所有拥有《阿拉巴斯坦王国》特征的角色力量+1000。
///
/// 实现说明：
///   - 纯持续力量修正 → ContinuousEffect.PowerDelta（OnEnterField 注册）。
///   - Scope 限我方角色；Filter 排除自身且需具《阿拉巴斯坦王国》特征；
///     Predicate 限"我方的回合中"。
/// </summary>
public class OP04_012_NefertariCobra : IScriptedEffect
{
    public string CardNumber => "OP04-012";

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
                Filter = c => c.Id != selfId && c.Info.HasKeyword("阿拉巴斯坦王国"),
            },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, c) => s.CurrentTurnPlayer == owner,
        });

        return Task.CompletedTask;
    }
}
