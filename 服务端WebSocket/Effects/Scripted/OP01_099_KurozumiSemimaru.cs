using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-099 黑炭蝉丸（角色 / 暗 / 和之国・黑炭家，cost2 power3000）
/// 我方"黑炭蝉丸"以外拥有《黑炭家》特征的角色，不会在战斗中被KO。
///
/// 实现说明：
///   - 持续"不会在战斗中被KO"光环，用 ContinuousEffect.KoGuard="battle" 注册（OnEnterField）。
///   - Predicate：我方一侧、拥有《黑炭家》特征、且名称非"黑炭蝉丸"的角色。
/// </summary>
public class OP01_099_KurozumiSemimaru : IScriptedEffect
{
    public string CardNumber => "OP01-099";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var selfId = self.Id;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            KoGuard = "battle",
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                card.Info.HasKeyword("黑炭家") &&
                !card.Info.NameContains("黑炭蝉丸"),
        });
        return Task.CompletedTask;
    }
}
