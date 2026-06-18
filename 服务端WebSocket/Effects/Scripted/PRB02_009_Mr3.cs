using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// PRB02-009 Mr.3（加尔迪诺）（角色，水）
/// 当此角色因对方的效果而转为休息状态时可以发动。将此角色放置到废弃区，可以抽取 2 张卡牌。【阻挡者】
///
/// 实现说明 / 简化点：
///   - 用反应式 watcher OnCharRested 触发（该 watcher 仅在效果横置 RestCard 时派发，已排除自身攻击宣言的横置）。
///   - "因对方的效果"近似为：触发发生在对方回合（CurrentTurnPlayer != OwnerIndex），
///     借此排除我方回合我方主动横置自己 Mr.3 的情形。watcher payload 不含横置来源方，故为近似。
///   - 仅当被横置的是本卡自身时发动（restedCardId == 自身）。
///   - "可以"为可选效果，用 ConfirmOptional 询问；确认后放置废弃区（走 BattleEngine.KOCard，不触发 KO 事件）+ 抽 2。
///   - 【阻挡者】为纯关键词，由引擎处理，此处仅实现主动效果部分。
/// </summary>
public class PRB02_009_Mr3 : IScriptedEffect
{
    public string CardNumber => "PRB02-009";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharRested;

    public async Task Resolve(EffectContext ctx)
    {
        // 「因对方的效果」近似：仅对方回合触发
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;

        // 仅本卡自身被横置时触发
        var restedId = ctx.Vars.TryGetValue("restedCardId", out var v) ? v as string : null;
        if (restedId != ctx.Source.Id.ToString()) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "Mr.3：将此角色放置到废弃区并抽取 2 张卡牌？");
        if (!use) return;

        // 放置废弃区（不触发 KO 事件）+ 抽 2
        BattleEngine.KOCard(ctx.State, ctx.OwnerIndex, ctx.Source);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
    }
}
