using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-053 约撒&强尼（角色）
/// 【咚!!×1】我方卡组不多于 20 张的场合，此角色的力量 +2000。
///
/// 实现说明：
///   - 纯持续力量修正，用 ContinuousEffect 注册（登场时一次）。
///   - 【咚!!×1】= 本卡被赋予中的咚!! ≥1；条件：我方卡组 ≤20 张。
///   - 仅作用于本卡自身。
/// </summary>
public class OP03_053_YosakuJohnny : IScriptedEffect
{
    public string CardNumber => "OP03-053";

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
                s.Players[owner].DeckCount <= 20,
        });

        return Task.CompletedTask;
    }
}
