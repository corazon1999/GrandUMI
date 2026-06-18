using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-084 谢泼德·十·庇特圣（角色 7 费 5000，天龙人/五老星）
/// 1. 我方废弃区中有 7 张或更多卡牌的场合，此角色不会因对方的效果而离场。（持续防离场，自身）
/// 2. 【我方的回合中】我方废弃区中有 10 张或更多卡牌的场合，将我方所有拥有《五老星》特征的
///    角色原本的力量变为 7000。
///
/// 实现说明 / 简化点：
///   - 第 1 条用 PreKO 钩子实现：此角色将要被 KO 时，若废弃区 ≥7 且非战斗结算（近似"因对方
///     效果"），调用 MarkPreventKO 取消本次 KO。PreKO 仅对本卡自身派发，正好覆盖自身防离场。
///     （引擎仅以 KO 表达"离场"，退回/放回卡组等其它离场无 PreKO，按惯例近似仅拦 KO。）
///   - 第 2 条"将原本的力量变为 7000"为对原本力量的持续覆盖：ContinuousEffect 仅支持力量增量
///     (PowerDelta)，无"持续设定原本力量为定值"的通道（OriginalPowerOverride 为回合内一次性
///     设值且每回合清空，无法随废弃区张数动态持续维持），故该条未实现。
/// </summary>
public class OP13_084_Peter : IScriptedEffect
{
    public string CardNumber => "OP13-084";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.PreKO;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 仅"因对方的效果"近似：非战斗结算中
        if (ctx.State.CurrentBattle is not null) return Task.CompletedTask;

        if (me.Trash.Count >= 7)
        {
            ctx.State.MarkPreventKO(self.Id);
        }

        return Task.CompletedTask;
    }
}
