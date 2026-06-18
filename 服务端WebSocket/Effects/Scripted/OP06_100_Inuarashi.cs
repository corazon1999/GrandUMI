using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-100 犬岚（角色 / 光 / 纯毛族・和之国・赤鞘九人男）
/// 【咚!!×2】【攻击时】可以丢弃我方的 1 张手牌：将对方最多 1 张费用不高于对方生命卡牌张数的角色 KO。
///
/// 说明 / 简化点：
///   - 触发节无 cost 通道，故以脚本实现"丢弃 1 张手牌"作为可选成本。
///   - 【咚!!×2】门槛：此角色被赋予的咚!! ≥ 2 时才发动（AttachedDonCount(self.Id)）。
///   - KO 目标费用阈值为动态值 = 对方生命卡牌张数（opp.LifeCount），在结算时计算。
///   - "可以"=可选：先 ConfirmOptional；玩家需有手牌可弃且有合法目标才提示。
/// </summary>
public class OP06_100_Inuarashi : IScriptedEffect
{
    public string CardNumber => "OP06-100";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×2】：此角色被赋予的咚!! 至少 2 张
        if (me.AttachedDonCount(self.Id) < 2) return;

        // 需要有 1 张手牌可作为成本丢弃
        if (me.Hand.Count == 0) return;

        // 动态阈值：对方生命卡牌张数
        int threshold = opp.LifeCount;
        var targets = opp.Characters.Where(c => c.Info.Cost <= threshold).ToList();
        if (targets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "犬岚【攻击时】：丢弃 1 张手牌，将对方 1 张费用≤对方生命数的角色 KO？");
        if (!use) return;

        // 成本：丢弃我方 1 张手牌
        var discardChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardHand",
            "选择 1 张要丢弃的手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (discardChoice.Count == 0) return; // 未支付成本
        var toDiscard = me.Hand.First(c => c.Id.ToString() == discardChoice[0]);
        AtomicOps.DiscardHand(me, toDiscard);

        // 效果：KO 对方最多 1 张费用不高于阈值的角色
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择 1 张要 KO 的对方角色（费用≤对方生命数）",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
