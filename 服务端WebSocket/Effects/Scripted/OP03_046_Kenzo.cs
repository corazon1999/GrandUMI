using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-046 健藏（角色）
/// 【阻挡者】（纯关键词，引擎处理）
/// 【对方的回合中】我方卡组不多于 20 张的场合，此角色的力量 +3000。
///
/// 实现说明：
///   - 纯持续力量修正，用 ContinuousEffect 注册（登场时一次）。
///   - 仅作用于本卡自身（Predicate 限定 card.Id==selfId）。
///   - 条件：对方回合中（CurrentTurnPlayer != owner）且我方卡组 ≤20 张。
/// </summary>
public class OP03_046_Kenzo : IScriptedEffect
{
    public string CardNumber => "OP03-046";

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
            PowerDelta = 3000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer != owner &&
                s.Players[owner].DeckCount <= 20,
        });

        return Task.CompletedTask;
    }
}
