using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-120 热海温泉（事件）
/// 【主要】对方生命卡牌为 4 张或更多的场合，将对方生命区最上方的最多 1 张卡牌放置到废弃区。
///
/// 说明 / 简化点：
///   - 条件：对方生命数 >= 4。
///   - 效果：对方生命顶 1 张移入对方废弃区（直接操作 LifeArea/Trash 列表）。
///   - 【触发】另行由触发关键词处理，不在此实现。
/// </summary>
public class OP03_120_AtamiHotSpring : IScriptedEffect
{
    public string CardNumber => "OP03-120";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (opp.LifeCount < 4) return Task.CompletedTask;

        if (opp.LifeArea.Count > 0)
        {
            var top = opp.LifeArea[0];
            opp.LifeArea.RemoveAt(0);
            opp.Trash.Add(top);
        }

        return Task.CompletedTask;
    }
}
