using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-054 蒙奇·D·戈普（角色 / 水 6 费 7000 / 海军）
/// 【咚!!×1】此角色在与拥有属性（打）的卡牌的战斗中不会被KO。
///
/// 实现说明：与 P-052 同型，条件改为攻击者属性含「打」。
///   - ContinuousEffect.KoGuard="battle"，谓词在 BattleEngine.KOCardAsync 评估时读 s.CurrentBattle
///     取出攻击者并判断属性，要求本卡被赋予中的咚!! ≥1。
/// </summary>
public class P_054_Garp : IScriptedEffect
{
    public string CardNumber => "P-054";

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
                return attacker.Info.Property.Contains("打");
            },
        });
        return Task.CompletedTask;
    }
}
