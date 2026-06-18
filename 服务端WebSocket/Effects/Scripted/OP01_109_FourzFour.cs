using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-109 福兹·弗（角色 / 暗 / 百兽海盗团，cost2 power3000）
/// 【咚!!×1】【我方的回合中】我方场上存在8张或更多咚!!的场合，此角色的力量+1000。
///
/// 实现说明：
///   - 纯持续力量修正，用 ContinuousEffect 注册（OnEnterField）。
///   - 条件：本卡被赋予咚!!≥1（【咚!!×1】）、我方回合中、且我方费用区合计咚!!≥8。
/// </summary>
public class OP01_109_FourzFour : IScriptedEffect
{
    public string CardNumber => "OP01-109";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var selfId = self.Id;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer == owner &&
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                s.Players[owner].TotalDonInCostArea >= 8,
        });
        return Task.CompletedTask;
    }
}
