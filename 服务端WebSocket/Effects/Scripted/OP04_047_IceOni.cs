using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-047 冰鬼（角色 / 水 / 0力）
/// 【我方的回合中】当此角色与对方费用不高于5的角色进行战斗的战斗结束时，将对方进行战斗的角色放回其持有者的卡组最下方。
///
/// 实现：监听 OnBattleEnd（GameEngine 在清场前派发，payload attackerId/targetCardId/targetIsLeader）。
/// 仅我方回合、且此角色参战（作为攻击者或被攻击目标）、对手参战角色当前费用≤5 时，将该对手角色放回卡组底。
/// </summary>
public class OP04_047_IceOni : IScriptedEffect
{
    public string CardNumber => "OP04-047";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnBattleEnd;

    public Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;   // 我方回合中

        var attackerId = ctx.Vars.TryGetValue("attackerId", out var av) ? av as string : null;
        var targetCardId = ctx.Vars.TryGetValue("targetCardId", out var tv) ? tv as string : null;
        bool targetIsLeader = ctx.Vars.TryGetValue("targetIsLeader", out var lv) && lv is bool lb && lb;

        // 确定对手参战角色：此角色为攻击者→目标；此角色为被攻击目标→攻击者
        string? oppCharId = null;
        if (attackerId == self.Id.ToString() && !targetIsLeader) oppCharId = targetCardId;
        else if (targetCardId == self.Id.ToString()) oppCharId = attackerId;
        if (oppCharId is null) return Task.CompletedTask;

        var oppChar = opp.Characters.FirstOrDefault(c => c.Id.ToString() == oppCharId);
        if (oppChar is null) return Task.CompletedTask;
        if (ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, oppChar) > 5) return Task.CompletedTask;

        AtomicOps.ReturnFieldToDeckBottom(ctx.State, 1 - ctx.OwnerIndex, oppChar);
        return Task.CompletedTask;
    }
}
