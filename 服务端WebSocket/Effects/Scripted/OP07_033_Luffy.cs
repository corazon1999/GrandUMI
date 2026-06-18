using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-033 蒙奇·D·路飞（角色）
/// 我方场上存在 3 张或更多角色的场合，我方"蒙奇·D·路飞"以外的费用不高于 3 的角色
/// 不会因对方的效果而被 KO。
///   （条件性全体持续防KO，用 ContinuousEffect.KoGuard="effect" + Predicate 实现。）
/// </summary>
public class OP07_033_Luffy : IScriptedEffect
{
    public string CardNumber => "OP07-033";

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
            KoGuard = "effect",
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner &&
                s.Players[owner].Characters.Count >= 3 &&
                !card.Info.NameContains("蒙奇·D·路飞") &&
                s.CurrentCostOf(sideIdx, card) <= 3,
        });

        return Task.CompletedTask;
    }
}
