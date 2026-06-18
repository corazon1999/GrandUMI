using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-004 撒谎布（角色）
/// 【咚!!×1】【我方的回合中】【每回合1次】当对方发动事件时，抽取1张卡牌。
///
/// 实现说明：
///   - 用 Wave3 的 OnOppEventPlayed 钩子（对方发动事件时派发）。
///   - 条件：仅我方回合中、【咚!!×1】(自身被赋予咚≥1)、每回合1次、且确为对方出牌。
/// </summary>
public class OP01_004_Usopp : IScriptedEffect
{
    public string CardNumber => "OP01-004";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppEventPlayed;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 仅我方回合中
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;

        // 确为对方发动事件
        var owner = ctx.Vars.TryGetValue("owner", out var v) && v is int oi ? oi : -1;
        if (owner == ctx.OwnerIndex) return Task.CompletedTask;

        // 【咚!!×1】
        if (me.AttachedDonCount(self.Id) < 1) return Task.CompletedTask;

        // 每回合1次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        return Task.CompletedTask;
    }
}
