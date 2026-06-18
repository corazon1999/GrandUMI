using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-032 阿修罗童子（角色）
/// 【咚!!×1】对方场上存在 2 张或更多休息状态的角色的场合，此角色的力量 +2000。
///
/// 实现说明：
///   - 纯持续/条件力量修正，用 ContinuousEffect 注册（OnEnterField 时机）。
///   - 条件【咚!!×1】：本角色被赋予 ≥1 张咚!!；以及对方场上 ≥2 张休息状态角色。
///   - 来源卡离场后引擎自动清理。
/// </summary>
public class OP01_032_AshuraDoji : IScriptedEffect
{
    public string CardNumber => "OP01-032";

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
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                s.Players[1 - owner].Characters.Count(c => c.IsTapped) >= 2,
        });

        return Task.CompletedTask;
    }
}
