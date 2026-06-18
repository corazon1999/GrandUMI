using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-112 梅迦罗（角色 / 光 3 费 2000，动物/鱼人岛）
/// 【阻挡者】（关键词，由引擎处理）
/// 【对方的回合中】我方领袖为"白星"的场合，此角色的力量 +4000。
///
/// 实现：
///   - 【阻挡者】为纯关键词，由引擎处理，无需脚本。
///   - 持续力量修正用 ContinuousEffect 注册：仅作用于本角色自身，
///     条件 = 当前为对方回合 且 我方领袖名为"白星"，满足时 +4000。
///   - 登场时注册；本卡离场时引擎自动清理。重复登场前先去重避免叠加。
/// </summary>
public class OP11_112_Megalo : IScriptedEffect
{
    public string CardNumber => "OP11-112";

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
                Filter = c => c.Id == selfId,
            },
            PowerDelta = 4000,
            Predicate = (s, sideIdx, card) =>
                s.CurrentTurnPlayer != owner &&
                s.Players[owner].Leader.MatchesName("白星"),
        });

        return Task.CompletedTask;
    }
}
