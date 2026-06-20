using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-114 S-老鹰（角色 / 光 / 艾格赫德·炽天使）
/// 【咚!!×1】我方生命卡牌张数少于对方的场合，此角色在与拥有属性（斩）的卡牌的战斗中不会被KO，
/// 且此角色的力量+2000。
///
/// 实现说明（OnEnterField 注册两条 ContinuousEffect，均限定本卡自身）：
///   - 条件：被赋予中的咚!! ≥ 1 且 我方生命数 < 对方生命数。
///   - 力量 +2000 → PowerDelta=2000，Predicate 限上述条件。
///   - 「在与斩属性卡的战斗中不会被KO」→ KoGuard="battle"，
///     Predicate 额外校验当前战斗 CurrentBattle 的攻击者属性为「斩」。
/// </summary>
public class OP08_114_SHawk : IScriptedEffect
{
    public string CardNumber => "OP08-114";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());

        // 基础条件：咚!!×1 且 我方生命 < 对方生命
        Func<GameState, bool> baseCond = s =>
            s.Players[owner].AttachedDonCount(selfId) >= 1 &&
            s.Players[owner].LifeCount < s.Players[1 - owner].LifeCount;

        // 力量 +2000
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) => card.Id == selfId && baseCond(s),
        });

        // 在与「斩」属性卡的战斗中不会被KO
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "battle",
            Predicate = (s, sideIdx, card) =>
            {
                if (card.Id != selfId || !baseCond(s)) return false;
                var b = s.CurrentBattle;
                if (b is null) return false;
                var atkP = s.Players[b.AttackerPlayerIndex];
                var attacker = atkP.Leader.Id == b.AttackerCardId
                    ? atkP.Leader
                    : atkP.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
                return attacker is not null && attacker.Info.Property == "斩";
            },
        });

        return Task.CompletedTask;
    }
}
