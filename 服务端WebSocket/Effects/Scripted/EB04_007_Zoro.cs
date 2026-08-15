using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-007 罗罗诺亚·佐罗（角色 / 炎 / 艾格赫德·草帽一伙）
/// 【登场时】直到下个对方的结束阶段结束时为止，我方领袖力量 +2000。
/// 【启动主要】【每回合1次】对方场上存在力量为 8000 或更高的角色的场合，
///   本回合中，此角色获得【速攻：角色】效果。
///
/// 实现说明：
///   - 【登场时】领袖 +2000「直到下个对方结束阶段」跨回合，用 ContinuousEffect + TurnCount 时效
///     （baseTurn..+2，即我方回合与紧接的对方回合内有效）。Scope 限定我方领袖。
///   - 【启动主要】条件「对方存在当前力量≥8000角色」用 CurrentPowerOf 判定；满足则本回合赋予自身
///     【速攻：角色】，登场回合仅可攻击角色。
///   - 每回合1次用 TurnOnceUsed。
/// </summary>
public class EB04_007_Zoro : IScriptedEffect
{
    public string CardNumber => "EB04-007";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            AtomicOps.AddPowerUntilOppEnd(me.Leader, 2000, owner);
            return Task.CompletedTask;
        }

        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            var key = self.Info.Number + "-act" + ":" + self.Id;
            if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;

            int oppIdx = 1 - owner;
            bool hasBig = opp.Characters.Any(c => ctx.State.CurrentPowerOf(oppIdx, c) >= 8000);
            if (!hasBig) return Task.CompletedTask;

            me.TurnOnceUsed.Add(key);
            AtomicOps.GiveKeyword(self, "登场回合可攻击角色", KeywordDuration.ThisTurn, ctx.OwnerIndex);
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
