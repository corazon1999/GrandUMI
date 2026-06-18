using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-006 萨卡（角色 / 炎 / 4费 / 5000 / FILM・阿斯卡岛）
/// 【咚!!×1】【攻击时】直到下个我方的回合开始时为止，此角色的力量+1000。
///   之后，当本回合结束时，将我方1张拥有《FILM》特征的角色放置到废弃区。
///
/// 实现说明 / 简化点：
///   - 用 OnAttackDeclare 钩子；条件【咚!!×1】= 自身被赋予中的咚!! ≥1。
///   - "直到下个我方回合开始时为止 +1000"近似为本回合 +1000（AddPowerThisTurn）。该加成在我方
///     回合结束时清除，与"持续到对方整个回合"略有出入，引擎无更长时效的力量通道，按惯例近似。
///   - "本回合结束时废弃我方1张FILM角色"通过 EndOfTurnTasks(Kind="TrashFilm") 延迟执行：
///     回合结束时优先废弃来源卡(本身为FILM)，否则任一FILM角色（引擎实现）。
/// </summary>
public class OP06_006_Saga : IScriptedEffect
{
    public string CardNumber => "OP06-006";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】：自身需有 ≥1 张被赋予中的咚!!
        if (me.AttachedDonCount(self.Id) < 1) return Task.CompletedTask;

        // 直到下个我方回合开始为止 +1000（近似为本回合 +1000）
        AtomicOps.AddPowerThisTurn(self, 1000);

        // 当本回合结束时，将我方1张拥有《FILM》特征的角色放置废弃区
        ctx.State.EndOfTurnTasks.Add(new EndTurnTask
        {
            Kind = "TrashFilm",
            SourceCardId = self.Id.ToString(),
            Owner = ctx.OwnerIndex,
        });

        return Task.CompletedTask;
    }
}
