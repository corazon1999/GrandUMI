using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-032 战国（角色 / 地 / 海军，cost5 power6000）
/// 【咚!!×1】【我方的回合中】对方所有的角色费用 -2。
///
/// 实现说明：
///   - 持续费用修正用 ContinuousEffect.CostDelta=-2，Scope=对方角色。
///   - 条件谓词：本卡被赋予中的咚!! ≥1（【咚!!×1】）且当前为我方回合。
/// </summary>
public class P_032_Sengoku : IScriptedEffect
{
    public string CardNumber => "P-032";

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
            Scope = new ContinuousScope { Side = 1, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = -2,
            Predicate = (s, sideIdx, card) =>
                s.CurrentTurnPlayer == owner &&
                s.Players[owner].AttachedDonCount(selfId) >= 1,
        });
        return Task.CompletedTask;
    }
}
