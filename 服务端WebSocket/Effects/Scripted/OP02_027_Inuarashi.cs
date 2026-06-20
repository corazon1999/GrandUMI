using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-027 犬岚（角色 / 风 3 费 4000）
/// 我方所有的咚!!为休息状态的场合，此角色不会因对方的效果而离开场上。
///
/// 实现说明：
///   - 持续"不会因效果离场" → ContinuousEffect.KoGuard="effect"（仅因效果KO/离场时守护）。
///   - 条件"我方所有咚!!为休息状态"：费用区存在咚且没有任何活跃/赋予中的咚（即全部为休息）。
///     用 Predicate 实时判定，仅守护本卡自身。
/// </summary>
public class OP02_027_Inuarashi : IScriptedEffect
{
    public string CardNumber => "OP02-027";

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
            KoGuard = "effect",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[owner].CostArea.Count > 0 &&
                s.Players[owner].CostArea.All(d => d.State == DonState.Rest),
        });

        return Task.CompletedTask;
    }
}
