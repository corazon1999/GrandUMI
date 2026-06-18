using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-030 堂吉诃德·罗西南德（角色 / 风 / 2费 / 1000 / 海军・堂吉诃德海盗团）
/// 【阻挡者】（关键词，由引擎处理）
/// 【对方的回合中】我方休息状态的角色将要被KO的场合，可以改为将此角色（自身）放置到废弃区，
///   使该角色不会被KO。
///
/// 实现说明：
///   - 仅实现主动置换部分；【阻挡者】关键词由引擎处理。
///   - 用 OnAllyWillBeKOd 守护者钩子（战斗KO时派发），ctx.OwnerIndex = 被KO卡所属方 = 我方。
///   - 条件：对方回合中、被守护角色处于休息状态（IsTapped）。
///   - 成本/置换：将此卡（罗西南德自身）放置废弃区，并 MarkPreventKO 取消对该角色的KO。
///   - 局限：守护仅在战斗KO时派发；效果KO/离场不在覆盖范围。
/// </summary>
public class OP05_030_Rosinante : IScriptedEffect
{
    public string CardNumber => "OP05-030";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source; // 罗西南德自身

        // 【对方的回合中】
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;

        // 取出被守护的目标
        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        if (victimId is null) return;
        if (victimId == self.Id.ToString()) return; // 不能替自己
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victim is null) return;

        // 仅守护处于休息状态的角色
        if (!victim.IsTapped) return;

        // 可选发动
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗西南德：将此角色放置到废弃区，使该休息状态的角色不会被KO？");
        if (!use) return;

        // 置换成本：将自身放置废弃区（归还附着咚 + 移入废弃区）
        foreach (var d in me.CostArea)
        {
            if (d.State == DonState.Attached && d.AttachedToCardId == self.Id)
            {
                d.State = DonState.Rest;
                d.AttachedToCardId = null;
            }
        }
        me.Characters.Remove(self);
        me.Trash.Add(self);

        // 使该角色不会被KO
        ctx.State.MarkPreventKO(victim.Id);
    }
}
