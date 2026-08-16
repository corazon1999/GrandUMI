using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-084 谢泼德·十·庇特圣（角色 7 费 5000，天龙人/五老星）
/// 1. 我方废弃区中有 7 张或更多卡牌的场合，此角色不会因对方的效果而离场。（持续防离场，自身）
/// 2. 【我方的回合中】我方废弃区中有 10 张或更多卡牌的场合，将我方所有拥有《五老星》特征的
///    角色原本的力量变为 7000。
///
/// 实现说明：
///   - 第 1 条使用 LeaveGuard="effect" 的自身持续防离场，统一覆盖效果 KO、回手和放回卡组等离场方式。
///   - 第 2 条使用 OriginalPowerOverride=7000 的持续效果，随回合方与废弃区张数动态判定。
/// </summary>
public class OP13_084_Peter : IScriptedEffect
{
    public string CardNumber => "OP13-084";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            int owner = ctx.OwnerIndex;
            ctx.State.ContinuousEffects.RemoveAll(e =>
                e.SourceCardId == self.Id.ToString() && e.LeaveGuard is not null);
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = self.Id.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                LeaveGuard = "effect",
                Predicate = (state, sideIdx, card) =>
                    sideIdx == owner && card.Id == self.Id && state.Players[owner].Trash.Count >= 7,
            });

            ctx.State.ContinuousEffects.RemoveAll(e =>
                e.SourceCardId == self.Id.ToString() && e.OriginalPowerOverride.HasValue);
            ctx.State.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = self.Id.ToString(),
                Scope = new ContinuousScope
                {
                    Side = 0,
                    IncludeLeader = false,
                    IncludeCharacters = true,
                    Filter = card => card.Info.HasKeyword("五老星"),
                },
                OriginalPowerOverride = 7000,
                Predicate = (state, sideIdx, card) =>
                    sideIdx == owner &&
                    state.CurrentTurnPlayer == owner &&
                    state.Players[owner].Trash.Count >= 10 &&
                    card.Info.HasKeyword("五老星"),
            });
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
