using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-066 甚平（角色，暗）
/// 【阻挡者】（关键词，由引擎处理）
/// 【对方的回合中】我方场上存在 10 张咚!! 的场合，此角色的力量 +1000。
///
/// 实现说明：
///   - 持续/静态力量修正用 ContinuousEffect 注册（OnEnterField 时机）。
///   - 仅作用于自身：Predicate 限定 card.Id == self 且当前为对方回合且我方费用区咚!!总数 ≥10。
///   - “我方场上存在 10 张咚!!”取费用区咚!!总数 TotalDonInCostArea（含活跃/休息/被赋予中）。
///   - 【阻挡者】为关键词，引擎自行处理，脚本只实现持续力量部分。
/// </summary>
public class OP05_066_Jinbe : IScriptedEffect
{
    public string CardNumber => "OP05-066";

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
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.CurrentTurnPlayer != ownerIndex &&
                s.Players[ownerIndex].TotalDonInCostArea >= 10,
        });

        return Task.CompletedTask;
    }
}
