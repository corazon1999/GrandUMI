using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-012 阿玉（角色 / 风 / 和之国）
/// 【启动主要】可以将此角色转为休息状态：
///   将对方最多1张费用不高于3且拥有《动物》或《SMILE》特征的角色，或对方最多1张咚!!，转为休息状态。
///
/// 实现说明：
///   - 成本为横置自身（RestCard）。
///   - 在「对方角色」与「对方咚!!」之间二选一作为目标：先用 ChooseOption 选分支，再选具体目标。
///   - 对方角色转休息 = RestCard；对方咚!!转休息 = 将一张活跃咚 State 设为 Rest。
/// </summary>
public class EB03_012_Otama : IScriptedEffect
{
    public string CardNumber => "EB03-012";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        var charCands = opp.Characters.Where(c =>
            c.Info.Cost <= 3 &&
            (c.Info.HasKeyword("动物") || c.Info.HasKeyword("SMILE")) &&
            !c.IsTapped).ToList();
        var activeDons = opp.CostArea.Where(d => d.State == DonState.Active).ToList();

        if (charCands.Count == 0 && activeDons.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿玉【启动主要】：横置此角色，将对方1张费用≤3的《动物》/《SMILE》角色 或 对方1张咚!! 转为休息状态？");
        if (!use) return;

        // 成本：横置自身
        AtomicOps.RestCard(self);

        // 二选一：角色 vs 咚!!（只有一类可选时直接走该类）
        int branch;
        if (charCands.Count > 0 && activeDons.Count > 0)
        {
            branch = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "选择要横置的对象", new[] { "对方角色", "对方咚!!" });
        }
        else
        {
            branch = charCands.Count > 0 ? 0 : 1;
        }

        if (branch == 0)
        {
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方1张费用≤3的《动物》/《SMILE》角色转为休息状态",
                charCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0)
            {
                var tgt = charCands.First(c => c.Id.ToString() == ch[0]);
                AtomicOps.RestCard(tgt);
            }
        }
        else
        {
            // 对方1张活跃咚转为休息状态
            var don = activeDons.FirstOrDefault();
            if (don is not null) don.State = DonState.Rest;
        }
    }
}
