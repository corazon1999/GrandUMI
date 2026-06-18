using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-114 波尔萨利诺（角色）
/// 【对方的回合中】此角色不会因效果而被KO，力量+1000。
/// 【阻挡者】（关键词，引擎自动处理，无需脚本）。
///
/// 实现说明：
///   - 登场时注册两条持续效果（来源同为本卡，离场由引擎自动清理）：
///     1) 仅对方回合中，本卡力量 +1000（ContinuousEffect.PowerDelta + Predicate 限对方回合）。
///     2) 仅对方回合中，本卡不会因效果被KO（ContinuousEffect.KoGuard="effect"）。
///   - 用 RemoveAll 去重，避免重复登场叠加。
/// </summary>
public class OP02_114_Borsalino : IScriptedEffect
{
    public string CardNumber => "OP02-114";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 对方回合中：力量 +1000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) => card.Id == selfId && s.CurrentTurnPlayer != owner,
        });

        // 对方回合中：不会因效果被KO
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "effect",
            Predicate = (s, sideIdx, card) => card.Id == selfId && s.CurrentTurnPlayer != owner,
        });

        return Task.CompletedTask;
    }
}
