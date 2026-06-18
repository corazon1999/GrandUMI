using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-032 匹卡（角色）
/// 【我方的回合结束时】①：将此角色转为活跃状态。
/// 【每回合1次】此角色将要被KO的场合，可以改为将我方最多1张"匹卡"以外的费用为3或更高的角色
///   转为休息状态，使此角色不会被KO。
///
/// 实现说明 / 简化点：
///   - 回合结束自身活跃：文本含"①"为支付1张咚的成本，触发节无法表达可选/必付成本，
///     惯例只实现收益（将自身转为活跃），不真正横置费用区咚。
///   - 替代KO通过 PreKO 钩子实现：将另一张"匹卡"以外费用≥3的角色转为休息后，调用 MarkPreventKO
///     取消本次对自身的KO。每回合1次用 TurnOnceUsed 控制。
/// </summary>
public class OP05_032_Pica : IScriptedEffect
{
    public string CardNumber => "OP05-032";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnMyTurnEnd || t == EffectTrigger.PreKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            // 将此角色转为活跃状态
            AtomicOps.ActivateCard(self);
            return;
        }

        // PreKO：替代KO
        var key = self.Info.Number + "-prekoQ" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        var candidates = me.Characters
            .Where(c => c.Id != self.Id && !c.Info.Name.Contains("匹卡") && c.Info.Cost >= 3)
            .ToList();
        if (candidates.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "匹卡：是否将我方1张\"匹卡\"以外、费用≥3的角色转为休息状态，以避免此角色被KO？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方1张\"匹卡\"以外、费用≥3的角色转为休息状态",
            candidates.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.RestCard(target);
        me.TurnOnceUsed.Add(key);
        ctx.State.MarkPreventKO(self.Id);
    }
}
