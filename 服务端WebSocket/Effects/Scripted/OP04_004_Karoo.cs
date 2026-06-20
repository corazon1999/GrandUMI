using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-004 卡鲁（角色 / 炎 / 动物・阿拉巴斯坦王国）
/// 【启动主要】可以将此角色转为休息状态：赋予我方所有拥有《阿拉巴斯坦王国》特征的
///   角色各最多 1 张休息状态的咚!!。
///
/// 实现说明：
///   - 成本为横置自身（启动主要）。可选发动，用 ConfirmOptional。
///   - 群体贴咚：对每个《阿拉巴斯坦王国》角色逐张调用 AttachDonFromCost(休息态)，
///     受费用区可用咚数量限制（AttachDonFromCost 内部按可用咚处理）。
/// </summary>
public class OP04_004_Karoo : IScriptedEffect
{
    public string CardNumber => "OP04-004";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 自身需为活跃状态才能横置发动
        if (self.IsTapped) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卡鲁【启动主要】：将此角色转为休息状态，赋予我方所有《阿拉巴斯坦王国》角色各 1 张休息咚?");
        if (!use) return;

        // 成本：横置自身
        AtomicOps.RestCard(self);

        // 效果：对每个《阿拉巴斯坦王国》角色赋予 1 张休息状态的咚!!
        var targets = me.Characters.Where(c => c.Info.HasKeyword("阿拉巴斯坦王国")).ToList();
        foreach (var c in targets)
        {
            AtomicOps.AttachDonFromCost(me, c.Id, 1, DonState.Rest);
        }
    }
}
