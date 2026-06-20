using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-007 蒙奇·D·路飞（角色 / 炎 / 超新星・草帽一伙，cost4 power5000）
/// 【咚!!×1】此角色在与拥有属性(打)的领袖和角色的战斗中不会被KO。
///
/// 实现：被赋予中的咚!! ≥1 且本次战斗的攻击者属性为「打」时，自身不会被战斗KO。
///   用 ContinuousEffect.KoGuard="battle"（仅战斗中 KO 查询），Predicate 限自身、咚≥1、
///   并通过 s.CurrentBattle 找到攻击者校验其 Property=="打"。
/// </summary>
public class P_007_Luffy : IScriptedEffect
{
    public string CardNumber => "P-007";

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
                if (card.Id != selfId) return false;
                if (s.Players[owner].AttachedDonCount(selfId) < 1) return false;
                var b = s.CurrentBattle;
                if (b is null) return false;
                var atk = s.Players[b.AttackerPlayerIndex];
                var attacker = atk.Leader.Id == b.AttackerCardId
                    ? atk.Leader
                    : atk.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
                return attacker is not null && attacker.Info.Property == "打";
            },
        });

        return Task.CompletedTask;
    }
}
