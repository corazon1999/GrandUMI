using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-092 蒙奇·D·路飞（角色）
/// 根据我方废弃区中卡牌的张数适用以下持续效果（均用 ContinuousEffect 注册）：
///   ・废弃区 ≥10 张：此角色原本的力量变为 9000、费用 +10
///       （PowerDelta = 9000 - 卡面 7000 = 2000；CostDelta = +10）
///   ・废弃区 ≥20 张：对方的回合中，我方领袖原本的力量变为 7000
///       （对领袖 PowerDelta = 7000 - 领袖卡面力量，Predicate 限对方回合）
///   ・废弃区 ≥30 张：此角色的力量 +1000
/// 三条共用同一 SourceCardId，离场时引擎一并清理。
/// 说明：「原本力量变为 N」以「PowerDelta = N - 卡面 Power」近似实现。
/// </summary>
public class OP15_092_MonkeyDLuffy : IScriptedEffect
{
    public string CardNumber => "OP15-092";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;
        var leaderBasePower = ctx.State.Players[owner].Leader.Info.Power;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // ≥10 张：此角色原本力量变为 9000（卡面 7000 → +2000）
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 9000 - self.Info.Power,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].Trash.Count >= 10,
        });

        // ≥10 张：此角色费用 +10
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = 10,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].Trash.Count >= 10,
        });

        // ≥20 张：对方回合中，我方领袖原本力量变为 7000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
            PowerDelta = 7000 - leaderBasePower,
            Predicate = (s, sideIdx, card) =>
                card.Id == s.Players[owner].Leader.Id &&
                s.CurrentTurnPlayer != owner &&
                s.Players[owner].Trash.Count >= 20,
        });

        // ≥30 张：此角色力量 +1000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 1000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId && s.Players[owner].Trash.Count >= 30,
        });

        return Task.CompletedTask;
    }
}
