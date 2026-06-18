using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-050 闪电（角色 / 水 cost4）
/// 我方手牌不多于 1 张的场合，此角色的力量 +2000。
/// 【阻挡者】（纯关键词，由引擎处理）
///
/// 实现说明：
///   - 条件性静态力量加成用 ContinuousEffect.PowerDelta 注册（OnEnterField 时机）。
///   - Predicate：仅对自身生效，且我方手牌张数 ≤1 时 +2000。来源离场后引擎自动清理。
///   - 【阻挡者】为纯关键词，引擎自行处理，无需脚本。
/// </summary>
public class OP02_050_Inazuma : IScriptedEffect
{
    public string CardNumber => "OP02-050";

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
            PowerDelta = 2000,
            Predicate = (s, sideIdx, c) =>
                c.Id == selfId && s.Players[owner].Hand.Count <= 1,
        });

        return Task.CompletedTask;
    }
}
