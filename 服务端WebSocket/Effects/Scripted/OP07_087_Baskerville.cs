using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-087 巴斯卡比尔（角色）
/// 【我方的回合中】对方场上存在费用为 0 的角色的场合，此角色的力量 +3000。
///
/// 实现说明 / 简化点：
///   - 纯持续力量修正，用 ContinuousEffect 注册（在 OnEnterField 时机），离场由引擎清理。
///   - 仅作用于自身（Filter 限定 card.Id == 自身），+3000。
///   - Predicate：当前为我方回合(s.CurrentTurnPlayer == owner) 且 对方场上有 CurrentCost()==0 的角色时生效。
/// </summary>
public class OP07_087_Baskerville : IScriptedEffect
{
    public string CardNumber => "OP07-087";

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
            PowerDelta = 3000,
            // 作用对象=此角色自身：Scope 仅供显示，引擎只看 Predicate，必须在此限定 card.Id==selfId，
            // 否则条件成立时全场（含领袖、对方）都 +3000。
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer == owner &&
                s.Players[1 - owner].Characters.Any(c => s.CurrentCostOf(c) == 0),
        });

        return Task.CompletedTask;
    }
}
