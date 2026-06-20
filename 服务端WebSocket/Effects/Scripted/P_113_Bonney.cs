using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-113 杰丽·邦妮（角色 / 光 / 邦妮海盗团，cost4 power4000，counter反击+2000）
/// 【咚!!×2】【对方回合中】此角色获得【阻挡者】效果，力量+2000。
///
/// 实现说明：
///   - 纯持续/静态条件修正：自身被赋予中的咚!! >=2 且在对方回合中时，
///     获得【阻挡者】(GrantKeyword) 且力量+2000(PowerDelta)。
///   - 用两条 ContinuousEffect 注册（OnEnterField），Predicate 实时判咚数与回合归属。
/// </summary>
public class P_113_Bonney : IScriptedEffect
{
    public string CardNumber => "P-113";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        bool Cond(GameState s) =>
            s.CurrentTurnPlayer != owner &&
            s.Players[owner].AttachedDonCount(selfId) >= 2;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 【咚!!×2】【对方回合中】获得【阻挡者】
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, card) => card.Id == selfId && Cond(s),
        });

        // 【咚!!×2】【对方回合中】力量+2000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) => card.Id == selfId && Cond(s),
        });

        return Task.CompletedTask;
    }
}
