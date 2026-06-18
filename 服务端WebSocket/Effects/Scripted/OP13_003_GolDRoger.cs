using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-003 高路·D·罗杰（领航 / 炎·暗）
/// 我方场上存在咚!!的场合，将1张我方咚!!阶段中将要放置的咚!!赋予我方领袖。
/// 我方场上咚!!的张数不多于9张的场合，此领袖的力量-2000。
/// 实现：
///   - 咚阶段重定向（将1张咚赋予领袖）由 TurnEngine.EnterDonPhase 特例处理。
///   - 本脚本 OnGameStart 注册持续力量：我方场上咚!!总数≤9 时此领袖 -2000。
/// </summary>
public class OP13_003_GolDRoger : IScriptedEffect
{
    public string CardNumber => "OP13-003";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnGameStart;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var sid = self.Id.ToString();
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == sid);
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = sid,
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = -2000,
            // 仅此领袖自身、且我方咚!!区总数(含赋予领袖的)≤9 时生效
            Predicate = (s, sideIdx, c) => sideIdx == owner && c.Id == self.Id
                && s.Players[owner].TotalDonInCostArea <= 9,
        });
        return Task.CompletedTask;
    }
}
