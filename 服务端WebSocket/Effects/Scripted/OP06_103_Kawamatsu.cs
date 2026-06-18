using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-103 河松（角色 / 光 / 鱼人族・和之国・赤鞘九人男）
/// 【攻击时】可以丢弃我方的 2 张手牌：将我方最多 1 张力量为 0 的角色，
///   正面朝上加入持有者生命区的最上方或最下方。
///
/// 说明 / 简化点：
///   - 触发节无 cost 通道，故脚本实现"丢弃 2 张手牌"作为可选成本。
///   - "力量为 0"取卡面原始力量 c.Info.Power == 0。
///   - 用 AtomicOps.MoveCharToLife(s, ownerIdx, card, toTop) 将角色置入生命区；
///     toTop 由玩家二选一（最上方 / 最下方）决定。
/// </summary>
public class OP06_103_Kawamatsu : IScriptedEffect
{
    public string CardNumber => "OP06-103";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本需要 2 张手牌
        if (me.Hand.Count < 2) return;

        // 目标：我方力量为 0 的角色
        var targets = me.Characters.Where(c => c.Info.Power == 0).ToList();
        if (targets.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "河松【攻击时】：丢弃 2 张手牌，将我方 1 张力量为 0 的角色加入生命区？");
        if (!use) return;

        // 成本：丢弃我方 2 张手牌
        var discardChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardHand",
            "选择 2 张要丢弃的手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 2, 2);
        if (discardChoice.Count < 2) return; // 未支付成本
        foreach (var cid in discardChoice)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card != null) AtomicOps.DiscardHand(me, card);
        }

        // 效果：选择 1 张力量为 0 的角色置入生命区（最多 1 张）
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张力量为 0 的角色加入生命区",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;
        var tgt = targets.First(c => c.Id.ToString() == chosen[0]);

        // 二选一：加入生命区最上方或最下方
        int pos = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将该角色加入生命区的位置", new[] { "最上方", "最下方" });
        bool toTop = pos == 0;
        AtomicOps.MoveCharToLife(ctx.State, ctx.OwnerIndex, tgt, toTop);
    }
}
