using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-098 阿特拉斯（角色 / 光 5 费 6000）
/// 我方生命卡牌的张数少于对方生命卡牌的张数的场合，此角色不会在战斗中被KO。
///
/// 实现说明 / 简化点：
///   - "不会在战斗中被KO" 用 PreKO 钩子实现：仅当当前处于战斗中（ctx.State.CurrentBattle != null）
///     且我方生命数 < 对方生命数时，调用 MarkPreventKO 取消本次对自身的 KO。
///   - 非战斗的效果KO（CurrentBattle == null）不拦截，符合"在战斗中"的限定。
///   - 此为置换型持续状态，依赖 PreKO 事件钩子按条件判定，无需 ContinuousEffect。
///   - 【触发】"我方领袖为贝加班克时此卡牌登场" 由触发机制(生命登场)单独处理，此处不实现。
/// </summary>
public class OP07_098_Atlas : IScriptedEffect
{
    public string CardNumber => "OP07-098";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.PreKO;

    public Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];

        // 仅在战斗中生效
        if (s.CurrentBattle == null) return Task.CompletedTask;

        // 我方生命数少于对方生命数时，不会被KO
        if (me.LifeCount < opp.LifeCount)
        {
            s.MarkPreventKO(ctx.Source.Id);
        }

        return Task.CompletedTask;
    }
}
