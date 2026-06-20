using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-052 杰拉基尔·米霍克（角色 / 水 6 费 7000 / 王下七武海）
/// 【咚!!×1】此角色在与拥有属性（斩）的卡牌的战斗中不会被KO。
///
/// 实现说明：
///   - 条件性战斗防KO：仅在「本卡被赋予中的咚!! ≥1」且「本次战斗的攻击者属性含斩」时生效。
///   - 用 ContinuousEffect.KoGuard="battle" 注册（OnEnterField）。
///   - KoGuard 的 "battle" 判定发生在 BattleEngine.KOCardAsync 内，此时 s.CurrentBattle 仍存在，
///     故谓词可从 CurrentBattle 取出攻击者并判断其属性，实现「与属性（斩）卡牌战斗中不被KO」。
///   - 离场由引擎自动清理；重复登场前先去重。
/// </summary>
public class P_052_Mihawk : IScriptedEffect
{
    public string CardNumber => "P-052";

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
            KoGuard = "battle",
            Predicate = (s, sideIdx, card) =>
            {
                if (card.Id != selfId || sideIdx != owner) return false;
                if (s.Players[owner].AttachedDonCount(selfId) < 1) return false;
                var b = s.CurrentBattle;
                if (b is null) return false;
                var atkP = s.Players[b.AttackerPlayerIndex];
                var attacker = atkP.Leader.Id == b.AttackerCardId ? atkP.Leader
                    : atkP.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
                if (attacker is null) return false;
                return attacker.Info.Property.Contains("斩");
            },
        });
        return Task.CompletedTask;
    }
}
