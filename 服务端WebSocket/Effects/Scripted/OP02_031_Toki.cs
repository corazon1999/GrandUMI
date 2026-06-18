using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-031 光月时（角色 / 风 1 费 0）
/// 我方场上存在角色"光月御殿"的场合，此角色获得【阻挡者】效果。
///
/// 实现说明：
///   - 条件性持续赋予关键词 → ContinuousEffect.GrantKeyword="阻挡者"。
///   - Predicate：仅本卡自身，且我方场上存在名为"光月御殿"的角色时授予。
/// </summary>
public class OP02_031_Toki : IScriptedEffect
{
    public string CardNumber => "OP02-031";

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
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && sideIdx == owner &&
                s.Players[owner].Characters.Any(c => c.MatchesName("光月御殿")),
        });

        return Task.CompletedTask;
    }
}
