using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-042 撒谎布（领航 4 费 5000，德莱斯罗兹/草帽一伙）
/// 1. 我方所有费用为 2 或更高且拥有《德莱斯罗兹》特征的角色费用 +1。（持续费用修正）
/// 2. 【对方的回合中】【每回合1次】当我方拥有《德莱斯罗兹》特征的角色被KO时，或因对方的效果
///    离开场上时，可以发动。我方手牌不多于 5 张的场合，抽取 1 张卡牌。
///
/// 本脚本实现能力 1（持续：我方《德莱斯罗兹》且费用≥2 的角色费用 +1），在领袖开局注册
/// ContinuousEffect.CostDelta = +1，离场/换领袖由引擎按 SourceCardId 清理。
///
/// 实现说明 / 简化点：
///   - "费用为 2 或更高"按卡面原始费用 c.Info.Cost >= 2 判定（修正前），避免自我递归引用。
///   - 能力 2 未实现并判为反应式：引擎当前无"我方角色被KO时 / 因对方效果离场时"的事件 watcher
///     （规范 13.6 反应式事件钩子：当角色因效果离场时无派发通道），故此触发型抽牌无法表达。
/// </summary>
public class OP10_042_Usopp : IScriptedEffect
{
    public string CardNumber => "OP10-042";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnGameStart;

    public Task Resolve(EffectContext ctx)
    {
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope
            {
                Side = 0,
                IncludeLeader = false,
                IncludeCharacters = true,
                Filter = c => c.Info.Cost >= 2 && c.Info.HasKeyword("德莱斯罗兹"),
            },
            CostDelta = 1,
            // Scope 仅供显示，作用对象必须写进 Predicate：仅我方场上、费用≥2 且《德莱斯罗兹》的角色，
            // 否则会漏到手牌/对方手牌/对方场上（#139）。
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner &&
                s.Players[owner].Characters.Contains(c) &&
                c.Info.Cost >= 2 && c.Info.HasKeyword("德莱斯罗兹"),
        });

        return Task.CompletedTask;
    }
}
