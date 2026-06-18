using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-005 沙奇（角色 / 炎 1 费 2000，白胡子海盗团）
/// 【启动主要】【每回合1次】本回合中，此角色的力量+2000。
///   之后，当本回合结束时，将此角色放置到废弃区。
///
/// 实现说明：
///   - 启动主要、每回合1次：TurnOnceUsed 去重。
///   - 本回合力量+2000：AddPowerThisTurn。
///   - "本回合结束时将此角色放置到废弃区"：入队 EndTurnTask{Kind="TrashFilm", SourceCardId=自身}，
///     TurnEngine 在回合结束阶段会优先按 SourceCardId 匹配并废弃该卡本身（不要求 FILM 特征）。
/// </summary>
public class OP03_005_Shakky : IScriptedEffect
{
    public string CardNumber => "OP03-005";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        // 本回合力量+2000
        AtomicOps.AddPowerThisTurn(self, 2000);

        // 本回合结束时将此角色放置到废弃区
        ctx.State.EndOfTurnTasks.Add(new EndTurnTask
        {
            Kind = "TrashFilm",
            SourceCardId = self.Id.ToString(),
            Owner = ctx.OwnerIndex,
        });

        return Task.CompletedTask;
    }
}
