using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-083 杰克斯（角色 / 炎 / 四皇·红发海盗团）
/// 【咚!!×1】【攻击时】可以丢弃我方手牌中的 1 张角色卡牌：
///   本回合中，对方最多 1 张角色力量 -1000。之后，抽取 1 张卡牌。
///
/// 实现说明：【咚!!×1】= 此角色需被赋予 ≥1 张咚!! 才可发动；成本为弃 1 张角色手牌（脚本实现）。
/// </summary>
public class P_083_Jacks : IScriptedEffect
{
    public string CardNumber => "P-083";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        // 【咚!!×1】：此角色需被赋予 ≥1 张咚!!
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;

        // 成本候选：手牌中的角色卡牌
        var charHand = me.Hand.Where(c => c.Info.Kind == CardKind.Character).ToList();
        if (charHand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "杰克斯【攻击时】：丢弃 1 张角色手牌，使对方 1 张角色本回合力量 -1000 并抽 1 张？");
        if (!use) return;

        var discardSel = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张角色手牌（成本）", charHand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (discardSel.Count == 0) return;
        var dcard = charHand.First(c => c.Id.ToString() == discardSel[0]);
        AtomicOps.DiscardHand(me, dcard);

        // 效果：对方最多 1 张角色力量 -1000
        var cands = opp.Characters;
        if (cands.Count > 0)
        {
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张角色，本回合力量 -1000",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.AddPowerThisTurn(tgt, -1000);
            }
        }

        // 之后：抽取 1 张卡牌
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
    }
}
