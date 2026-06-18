using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-033 妮古·罗宾（角色 / 风 4 费 5000，时光旅诗/草帽一伙）
/// 【登场时】我方场上存在 2 张或更多处于休息状态的角色的场合，直到下个对方的回合结束时为止，
///   我方所有拥有《时光旅诗》或《草帽一伙》特征的角色不会因效果而被 KO。
///
/// 实现说明 / 简化点：
///   - 群体持续"不会因效果被 KO" 用 ContinuousEffect.KoGuard = "effect"（参见规范 13.2）。
///   - "直到下个对方的回合结束时为止" 用基于回合数的 Predicate 自动到期（参见 OP07-018/099）：
///     登场若发生在我方回合，到期回合 = 当前回合；若在对方回合，则 = 下一回合。
///   - Scope.Side = 0（我方），仅角色（IncludeLeader=false），Filter 限定到所需特征。
///   - 前提：发动时我方休息状态角色 ≥2，否则不注册。
/// </summary>
public class OP09_033_NicoRobin : IScriptedEffect
{
    public string CardNumber => "OP09-033";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        // 前提：我方场上存在 2 张或更多处于休息状态的角色
        int restedChars = me.Characters.Count(c => c.IsTapped);
        if (restedChars < 2) return Task.CompletedTask;

        int expireTurn = ctx.State.CurrentTurnPlayer == owner
            ? ctx.State.TurnCount
            : ctx.State.TurnCount + 1;

        var selfId = self.Id;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Info.HasKeyword("时光旅诗") || c.Info.HasKeyword("草帽一伙"),
            },
            KoGuard = "effect",
            Predicate = (s, sideIdx, card) => sideIdx == owner && s.TurnCount <= expireTurn,
        });

        return Task.CompletedTask;
    }
}
