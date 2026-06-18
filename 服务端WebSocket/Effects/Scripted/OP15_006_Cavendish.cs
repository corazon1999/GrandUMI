using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-006 凯文迪修（角色）
/// 持续：我方废弃区中有 4 张或更多事件的场合，此角色的力量 +2000。
///   （ContinuousEffect 注册，引擎在每次评估力量时按条件累加。）
/// </summary>
public class OP15_006_Cavendish : IScriptedEffect
{
    public string CardNumber => "OP15-006";

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
                s.Players[owner].Trash.Count(c => c.Info.Kind == CardKind.Event) >= 4,
        });

        return Task.CompletedTask;
    }
}
