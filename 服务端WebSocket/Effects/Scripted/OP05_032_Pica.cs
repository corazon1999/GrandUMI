using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-032 匹卡（角色）
/// 【我方的回合结束时】①：将此角色转为活跃状态。
/// 【每回合1次】此角色将要被KO的场合，可以改为将我方最多1张"匹卡"以外的费用为3或更高的角色
///   转为休息状态，使此角色不会被KO。
///
/// 实现说明：
///   - 回合结束时横置 1 张未被赋予的活跃咚支付成本，再将自身转为活跃。
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
            var costDon = me.CostArea.FirstOrDefault(d => d.State == DonState.Active && d.AttachedToCardId is null);
            if (costDon is null) return;
            costDon.State = DonState.Rest;
            AtomicOps.ActivateCard(self);
            return;
        }

        // PreKO：替代KO
        var key = self.Info.Number + "-prekoQ" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        var candidates = me.Characters
            .Where(c => c.Id != self.Id && !c.Info.Name.Contains("匹卡")
                && ctx.State.CurrentCostOf(c) >= 3 && !c.IsTapped && AtomicOps.CanRestCard(ctx.State, c))
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
        if (!AtomicOps.RestCard(target)) return;
        me.TurnOnceUsed.Add(key);
        ctx.State.MarkPreventKO(self.Id);
    }
}
