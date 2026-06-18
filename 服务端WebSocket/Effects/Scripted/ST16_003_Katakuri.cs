using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST16-003 夏洛特·卡塔库栗（角色）
/// 我方领袖拥有《FILM》特征，且我方场上存在6张或更多处于休息状态的卡牌的场合，此角色的力量+2000。（持续）
/// 休息卡牌 = 休息角色 + 休息咚 + 休息舞台。
/// </summary>
public class ST16_003_Katakuri : IScriptedEffect
{
    public string CardNumber => "ST16-003";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            Predicate = (s, side, card) =>
            {
                if (card.Id != selfId) return false;
                var me = s.Players[owner];
                if (!me.Leader.Info.HasKeyword("FILM")) return false;
                int rested = me.Characters.Count(c => c.IsTapped)
                             + me.CostArea.Count(d => d.State == DonState.Rest)
                             + (me.StageCard is not null && me.StageCard.IsTapped ? 1 : 0);
                return rested >= 6;
            },
        });
        return Task.CompletedTask;
    }
}
