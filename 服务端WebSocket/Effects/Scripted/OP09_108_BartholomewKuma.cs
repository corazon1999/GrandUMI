using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-108 巴索罗缪·大熊（角色 / 光 4 费 5000，王下七武海/革命军）
///
/// 完整文本：
///   （本体无效果）
///   【触发】我方领袖拥有《革命军》特征，且双方生命卡牌合计张数不多于 5 张的场合，此卡牌登场。
///
/// 实现：
///   - 生命牌【触发】(OnLifeRevealTrigger)：触发发动时本卡已被放入废弃区。
///   - 条件：我方领袖含《革命军》且双方生命合计 ≤5 时，从废弃区免费以活跃状态登场。
///
/// 简化点：trigger 节 DSL 不支持 if 条件，故用脚本判定领袖特征与双方生命合计。
/// </summary>
public class OP09_108_BartholomewKuma : IScriptedEffect
{
    public string CardNumber => "OP09-108";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];

        // 条件：领袖含《革命军》且双方生命合计 ≤5
        if (!me.Leader.Info.HasKeyword("革命军")) return Task.CompletedTask;
        if (me.LifeCount + opp.LifeCount > 5) return Task.CompletedTask;

        // 此卡牌登场：触发发动时本卡在废弃区，从废弃区免费以活跃状态登场
        if (!me.Trash.Contains(ctx.Source)) return Task.CompletedTask;
        AtomicOps.PlayFromTrashFree(s, ctx.OwnerIndex, ctx.Source, restState: false);

        return Task.CompletedTask;
    }
}
