using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-114 夏洛特·玲玲（角色）
/// 【登场时】我方领袖拥有《大妈海盗团》特征的场合，将我方卡组最上方的最多 1 张卡牌加入生命区最上方。
///   之后，将对方生命区最上方的最多 1 张卡牌放置到废弃区。
///
/// 说明 / 简化点：
///   - 条件：领袖特征含《大妈海盗团》。
///   - 效果 1：卡组顶 1 张入我方生命顶（AddLifeFromDeckTop）。
///   - 效果 2：对方生命顶 1 张移入对方废弃区（直接操作 LifeArea/Trash 列表）。
/// </summary>
public class OP03_114_BigMom : IScriptedEffect
{
    public string CardNumber => "OP03-114";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (!me.Leader.Info.HasKeyword("大妈海盗团")) return Task.CompletedTask;

        // 效果 1：卡组顶最多 1 张加入我方生命区最上方
        AtomicOps.AddLifeFromDeckTop(me, 1);

        // 效果 2：对方生命区最上方最多 1 张放置到废弃区
        if (opp.LifeArea.Count > 0)
        {
            var top = opp.LifeArea[0];
            opp.LifeArea.RemoveAt(0);
            opp.Trash.Add(top);
        }

        return Task.CompletedTask;
    }
}
