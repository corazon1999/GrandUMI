using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-004 卡丽娜（角色 / 风 / FILM・大德索罗号）
/// 【阻挡者】（关键词，引擎处理）
/// 【对方的回合中】我方领袖为多种颜色，且我方场上没有原本力量6000或更高的角色的场合，此角色力量+4000。
///
/// 实现说明：纯持续力量修正，用 ContinuousEffect 注册（OnEnterField）。
///   Predicate：仅作用自身、对方回合中、领袖颜色数≥2、且我方角色无 Info.Power>=6000。
/// </summary>
public class EB03_004_Karina : IScriptedEffect
{
    public string CardNumber => "EB03-004";

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
            PowerDelta = 4000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer != owner &&
                s.Players[owner].Leader.Info.ColorList.Length >= 2 &&
                !s.Players[owner].Characters.Any(c => c.Info.Power >= 6000),
        });

        return Task.CompletedTask;
    }
}
