using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-012 熊国王（角色 / 炎 5 费 6000 / FILM·扑克牌海盗团）
/// 对方场上存在原本的力量为 6000 或更高的领袖或角色的场合，此角色在战斗中不会被KO。
///
/// 实现：注册条件性 KoGuard="battle" 的持续效果，谓词限定本卡自身，
/// 条件为对方领袖或角色中存在 Info.Power（原本力量）≥6000 者。
/// </summary>
public class OP06_012_KingdomOfBears : IScriptedEffect
{
    public string CardNumber => "OP06-012";

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
            Predicate = (s, sideIdx, c) =>
            {
                if (!(sideIdx == owner && c.Id == selfId)) return false;
                var opp = s.Players[1 - owner];
                if (opp.Leader.Info.Power >= 6000) return true;
                return opp.Characters.Any(ch => ch.Info.Power >= 6000);
            },
        });

        return Task.CompletedTask;
    }
}
