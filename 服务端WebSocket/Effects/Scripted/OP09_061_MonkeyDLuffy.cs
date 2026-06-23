using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-061 蒙奇·D·路飞（领航）
/// 【咚!!×1】我方所有角色费用+1。
/// 【我方的回合中】【每回合1次】当我方场上2张或更多咚!!放回咚!!卡组时，
///   从咚!!卡组中追加最多1张活跃状态的咚!!，再追加最多1张休息状态的咚!!。
///
/// 实现说明：
///   - 第一段为持续费用修正：用 ContinuousEffect.CostDelta=+1，作用于我方角色，
///     条件为此领袖身上附着的咚!! ≥1（【咚!!×1】）。OnGameStart 注册。
///   - 第二段用 Wave2 反应式 watcher OnDonReturnedToDeck，仅在我方回合且本次放回≥2张、每回合1次。
/// </summary>
public class OP09_061_MonkeyDLuffy : IScriptedEffect
{
    public string CardNumber => "OP09-061";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnGameStart || t == EffectTrigger.OnDonReturnedToDeck;

    public Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnGameStart)
        {
            RegisterContinuous(ctx);
            return Task.CompletedTask;
        }

        // OnDonReturnedToDeck
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【我方的回合中】
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;

        // 本次放回需 ≥2 张
        int count = ctx.Vars.TryGetValue("count", out var cv) && cv is int ci ? ci : 0;
        if (count < 2) return Task.CompletedTask;

        // 【每回合1次】
        var key = CardNumber + "-donreturn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        // 追加最多1张活跃 + 最多1张休息
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        return Task.CompletedTask;
    }

    private static void RegisterContinuous(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = 1,
            // 【咚!!×1】：此领袖身上附着的咚!! ≥1 时生效。
            // Scope 仅供显示，作用对象必须写进 Predicate：仅我方场上角色，
            // 否则会漏到对方场上/双方手牌（#152）。
            Predicate = (s, sideIdx, card) =>
                s.Players[owner].AttachedDonCount(selfId) >= 1 &&
                sideIdx == owner &&
                s.Players[owner].Characters.Contains(card),
        });
    }
}
