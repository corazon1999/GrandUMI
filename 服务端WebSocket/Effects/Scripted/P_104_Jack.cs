using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-104 杰克斯（角色 / 地 / 四皇・红发海盗团・原罗杰海盗团，cost8 power10000）
/// 我方或对方场上存在 10 张咚!! 的场合，此角色不会因对方的效果而离场。
///
/// 实现说明：
///   - 持续防离场用 ContinuousEffect.LeaveGuard="effect"（仅作用于自身）。
///   - 条件「我方或对方场上存在 10 张咚!!」近似为任一方费用区咚总数 >=10。
///   - "对方的效果"近似为"因效果离场"（自伤罕见），与引擎离场守护语义一致。
///   - OnEnterField 注册，来源离场由引擎清理。
/// </summary>
public class P_104_Jack : IScriptedEffect
{
    public string CardNumber => "P-104";

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
            LeaveGuard = "effect",
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                (s.Players[owner].CostArea.Count >= 10 ||
                 s.Players[1 - owner].CostArea.Count >= 10),
        });

        return Task.CompletedTask;
    }
}
