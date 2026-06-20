using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-025 斯摩格（角色 / 地 / 海军，cost3 power5000）
/// 【咚!!×1】此角色不会在与"没有属性(特)"的角色的战斗中被 KO。
///
/// 实现说明：
///   - 用 ContinuousEffect.KoGuard="battle"（仅战斗中防 KO）注册，仅作用于本卡自身。
///   - 条件谓词：本卡被赋予中的咚!! ≥1（【咚!!×1】），且当前战斗中与本卡对战的另一方
///     角色"没有属性(Property 为空)"。领袖也无属性，但与领袖战斗时本卡是防守方
///     不会被 KO，攻击领袖时本卡为攻击者亦不会被 KO，按"无属性"包含领袖处理与卡面一致。
/// </summary>
public class P_025_Smoker : IScriptedEffect
{
    public string CardNumber => "P-025";

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
                if (s.Players[owner].AttachedDonCount(selfId) < 1) return false; // 【咚!!×1】
                var b = s.CurrentBattle;
                if (b is null) return false;
                // 找到与本卡对战的另一方战斗角色
                CardInstance? foe = null;
                if (b.AttackerCardId == selfId)
                {
                    if (b.TargetIsLeader) foe = s.Players[b.DefenderPlayerIndex].Leader;
                    else if (b.TargetCardId is Guid tid)
                        foe = s.Players[b.DefenderPlayerIndex].Characters.FirstOrDefault(c => c.Id == tid);
                }
                else if ((b.TargetCardId == selfId) ||
                         (b.ReplacedByBlockerCardId == selfId))
                {
                    var atk = s.Players[b.AttackerPlayerIndex];
                    foe = atk.Leader.Id == b.AttackerCardId
                        ? atk.Leader
                        : atk.Characters.FirstOrDefault(c => c.Id == b.AttackerCardId);
                }
                if (foe is null) return false;
                return string.IsNullOrEmpty(foe.Info.Property); // 对手无属性
            },
        });
        return Task.CompletedTask;
    }
}
