using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST02-014 X·德雷克（角色）
/// 【咚!!×1】【我方的回合中】此角色处于休息状态的场合，我方拥有《超新星》或《海军》特征的领袖和角色力量+1000。（持续）
/// </summary>
public class ST02_014_Drake : IScriptedEffect
{
    public string CardNumber => "ST02-014";
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
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = true },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
            {
                if (sideIdx != owner) return false;
                var me = s.Players[owner];
                if (!(me.AttachedDonCount(selfId) >= 1 && s.CurrentTurnPlayer == owner && self.IsTapped)) return false;
                return card.Info.HasKeyword("超新星") || card.Info.HasKeyword("海军");
            },
        });
        return Task.CompletedTask;
    }
}
