using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-033 船精灵（角色 / 暗 / 精灵，cost1 power0）
/// 我方场上存在"前进·梅利号"的场合，此角色获得【阻挡者】效果。
///
/// 实现说明：
///   - 条件性持续静态关键词：用 ContinuousEffect.GrantKeyword 注册（OnEnterField 时机），
///     Predicate 限定本卡自身，且我方场上存在名为"前进·梅利号"的卡（舞台或角色）。
///   - "我方场上"包含舞台区(StageCard)与角色区(Characters)；前进·梅利号为舞台卡。
/// </summary>
public class EB02_033_ShipSpirit : IScriptedEffect
{
    public string CardNumber => "EB02-033";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int ownerIndex = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                sideIdx == ownerIndex &&
                (
                    (s.Players[ownerIndex].StageCard is { } st && st.Info.NameContains("前进·梅利号")) ||
                    s.Players[ownerIndex].Characters.Any(c => c.Info.NameContains("前进·梅利号"))
                ),
        });

        return Task.CompletedTask;
    }
}
