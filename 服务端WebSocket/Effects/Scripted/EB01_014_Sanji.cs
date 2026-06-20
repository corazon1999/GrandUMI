using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-014 山智（角色 / 风 / FILM·草帽一伙）
/// 【咚!!×1】【我方的回合中】我方场上每存在3张休息状态的咚!!，此角色的力量+1000。
///
/// 实现说明：
///   - 动态值"每3张休息咚+1000"用 3 条 ContinuousEffect 累加实现：
///     休息咚≥3 加1000、≥6 再加1000、≥9 再加1000（费用区上限10张，故最多 +3000，
///     与 floor(休息咚数/3)*1000 等价）。
///   - 仅作用于自身、且仅我方回合中生效。
/// </summary>
public class EB01_014_Sanji : IScriptedEffect
{
    public string CardNumber => "EB01-014";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        foreach (var threshold in new[] { 3, 6, 9 })
        {
            int th = threshold;
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                PowerDelta = 1000,
                Predicate = (s, sideIdx, card) =>
                    card.Id == selfId &&
                    s.CurrentTurnPlayer == owner &&
                    s.Players[owner].CostArea.Count(d => d.State == DonState.Rest) >= th,
            });
        }

        return Task.CompletedTask;
    }
}
