using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-002 闪电（角色 / 炎 4 费 5000 / 革命军）
/// 此角色的力量为 7000 或更高的场合，此角色获得【流放】效果。
///
/// 实现：注册条件性 GrantKeyword="流放" 的持续效果，谓词限定为本卡自身，
/// 并以引擎评估的当前力量 CurrentPowerOf ≥ 7000 为条件。
/// 【流放】已被引擎消费（给予伤害时不发动触发直接废弃）。
/// </summary>
public class OP06_002_Flash : IScriptedEffect
{
    public string CardNumber => "OP06-002";

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
            GrantKeyword = "流放",
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId &&
                s.CurrentPowerOf(sideIdx, c) >= 7000,
        });

        return Task.CompletedTask;
    }
}
