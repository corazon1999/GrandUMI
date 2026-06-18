using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// PRB02-005 蒙奇·D·路飞（角色 / 风 / 超新星・草帽一伙 / cost4 power5000）
/// 【我方的回合中】【登场时】我方领袖为多种颜色且对方场上咚!!不多于 7 张的场合，
///   下个对方的主要阶段开始时，对方将其 1 张活跃状态的咚!!转为休息状态。
///
/// 实现说明：
///   - 登场时（仅我方回合）校验：领袖多色（ColorList.Length≥2）、对方咚总数≤7。
///   - 满足则向 GameState.NextOppMainPhaseTasks 登记 1 个延迟任务（Kind="RestOneActiveDon"）。
///     该任务在 GameEngine.EndTurnAsync 切到"对方回合"开始时（AdvanceTurn 后、咚已 reset 活跃）执行：
///     令对方 1 张未被赋予的活跃咚转为休息状态。简化：自动选第 1 张，无玩家交互演出。
/// </summary>
public class PRB02_005_Luffy : IScriptedEffect
{
    public string CardNumber => "PRB02-005";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 【我方的回合中】
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;
        // 我方领袖为多种颜色
        if (me.Leader.Info.ColorList.Length < 2) return Task.CompletedTask;
        // 对方场上咚!!不多于 7 张
        if (opp.TotalDonInCostArea > 7) return Task.CompletedTask;

        ctx.State.NextOppMainPhaseTasks.Add(new NextOppMainPhaseTask
        {
            Kind = "RestOneActiveDon",
            Owner = ctx.OwnerIndex,
            SourceCardId = ctx.Source.Id.ToString(),
        });
        return Task.CompletedTask;
    }
}
