using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-032 巴奇（角色）
/// 此角色在与拥有属性（斩）的卡牌的战斗中不会被KO。
///
/// 实现说明：
///   - 持续战斗防KO用 ContinuousEffect.KoGuard="battle"，Predicate 仅在本卡参与战斗、
///     且战斗对方卡牌的属性含"斩"时生效（与 ST08-002 同构思路）。
///   - 取战斗对方卡牌：本卡为攻击方时对方=防守目标(领袖/角色)；本卡为防守方时对方=攻击者。
/// </summary>
public class OP03_032_Buggy : IScriptedEffect
{
    public string CardNumber => "OP03-032";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var selfId = ctx.Source.Id;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "battle",
            Predicate = (s, sideIdx, card) =>
            {
                if (card.Id != selfId) return false;
                var b = s.CurrentBattle;
                if (b is null) return false;

                CardInstance? foe = null;
                var atkPlayer = s.Players[b.AttackerPlayerIndex];
                var defPlayer = s.Players[b.DefenderPlayerIndex];

                if (b.AttackerCardId == selfId)
                {
                    // 本卡为攻击方 → 对方为防守目标
                    if (b.TargetIsLeader) foe = defPlayer.Leader;
                    else
                    {
                        var tid = b.ReplacedByBlockerCardId ?? b.TargetCardId;
                        if (tid is { } g)
                            foe = defPlayer.Leader.Id == g ? defPlayer.Leader
                                : defPlayer.Characters.FirstOrDefault(c => c.Id == g);
                    }
                }
                else
                {
                    // 本卡为防守方(被攻击/阻挡) → 对方为攻击者
                    foe = atkPlayer.Leader.Id == b.AttackerCardId ? atkPlayer.Leader
                        : atkPlayer.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
                }

                return foe is not null && foe.Info.Property.Contains("斩");
            },
        });
        return Task.CompletedTask;
    }
}
