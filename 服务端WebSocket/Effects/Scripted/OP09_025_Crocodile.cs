using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-025 克洛克达尔（角色）
/// 我方领袖拥有《时光旅诗》特征的场合，此角色在与领袖的战斗中不会被KO。
/// 实现说明：条件性持续"战斗中不被KO"→ ContinuousEffect.KoGuard="battle"。
///   引擎 KoGuard 无法区分对手是领袖还是角色，这里近似为"领袖含《时光旅诗》时本卡战斗中不被KO"。
/// </summary>
public class OP09_025_Crocodile : IScriptedEffect
{
    public string CardNumber => "OP09-025";

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
                c.Id == selfId && s.Players[owner].Leader.Info.HasKeyword("时光旅诗"),
        });

        return Task.CompletedTask;
    }
}
