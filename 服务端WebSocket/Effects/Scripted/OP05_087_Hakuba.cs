using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-087 白马（角色）
/// 【咚!!×1】【攻击时】可以将我方 1 张此角色以外的角色 KO：
///   本回合中，对方最多 1 张角色费用 -5。
///
/// 实现说明 / 简化点：
///   - 【咚!!×1】为发动条件（需贴 1 张咚的状态）；引擎是否预检由调用方处理，这里仅实现收益逻辑。
///   - "可以"=可选发动；成本为"KO 我方 1 张此角色以外的角色"。
///   - 收益：对方最多 1 张角色本回合费用 -5（AddCostModifier，KeywordDuration.ThisTurn）。
/// </summary>
public class OP05_087_Hakuba : IScriptedEffect
{
    public string CardNumber => "OP05-087";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】：本卡需被赋予咚≥1才发动（引擎不预检攻击时咚门槛，须脚本自检）
        if (me.AttachedDonCount(self.Id) < 1) return;

        // 成本目标：我方此角色以外的角色
        var costTargets = me.Characters.Where(c => c.Id != self.Id).ToList();
        if (costTargets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "白马【攻击时】：KO 我方 1 张此角色以外的角色，使对方最多 1 张角色本回合费用 -5？");
        if (!use) return;

        // 成本：KO 我方 1 张此角色以外的角色
        var koPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张此角色以外的我方角色 KO（成本）",
            costTargets.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (koPick.Count == 0) return; // 未支付成本
        var sacrifice = costTargets.First(c => c.Id.ToString() == koPick[0]);
        AtomicOps.KO(ctx.State, ctx.OwnerIndex, sacrifice);

        // 收益：对方最多 1 张角色本回合费用 -5
        if (opp.Characters.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合费用 -5",
            opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = opp.Characters.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddCostModifier(tgt, -5, KeywordDuration.ThisTurn);
        }
    }
}
