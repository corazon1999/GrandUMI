using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-081 佳妮法（角色 / 地 4 费 5000，CP0）
/// 【咚!!×1】【我方的回合中】对方所有角色费用-1。
///
/// 实现说明：
///   - 持续费用修正：用 ContinuousEffect.CostDelta = -1。
///   - 条件【咚!!×1】：此角色被赋予中的咚 ≥1；【我方的回合中】：当前回合玩家为我方。
///   - Scope.Side = 1（对方），仅角色（IncludeCharacters=true, IncludeLeader=false）。
///   - 在 OnEnterField 注册，离场由引擎清理；重复登场前去重。
/// </summary>
public class OP07_081_Jabra : IScriptedEffect
{
    public string CardNumber => "OP07-081";

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
            Scope = new ContinuousScope
            {
                Side = 1,                  // 对方
                IncludeLeader = false,
                IncludeCharacters = true,
            },
            CostDelta = -1,
            Predicate = (s, sideIdx, card) =>
                s.CurrentTurnPlayer == owner &&                                 // 我方的回合中
                s.Players[owner].AttachedDonCount(selfId) >= 1,                 // 【咚!!×1】
        });

        return Task.CompletedTask;
    }
}
