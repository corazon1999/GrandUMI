using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-099 亚马逊（角色）
/// 【对方的攻击时】可以将此角色转为休息状态：对方可以将其生命区最上方的 1 张卡牌放置到废弃区。
///   没有那样做的场合，本回合中，对方最多 1 张领袖或角色力量-2000。
///
/// 实现说明 / 简化点：
///   - "可以将此角色转为休息状态"为可选成本：用 ConfirmOptional 询问发动后 RestCard 自身。
///     若自身已处于休息状态则无法支付成本，不发动。
///   - "对方可以将其生命区最上方 1 张卡牌放置到废弃区"：通过对对方发起 ChooseOption 让对方
///     在"弃生命/不弃"之间选择（规范 12.5 对方决策驱动）。
///   - 对方选择弃生命：将对方生命区最上方 1 张移入对方废弃区，效果到此结束。
///   - 对方选择不弃：由我方选择对方最多 1 张领袖或角色，本回合力量-2000。
/// </summary>
public class OP05_099_Amazon : IScriptedEffect
{
    public string CardNumber => "OP05-099";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本：可以将此角色转为休息状态（已休息则无法支付）
        if (self.IsTapped) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "亚马逊【对方的攻击时】：将此角色转为休息状态发动效果？");
        if (!use) return;

        AtomicOps.RestCard(self);

        // 对方决策：是否将其生命区最上方 1 张卡牌放置到废弃区
        int oppChoice = 0;
        if (opp.LifeArea.Count > 0)
        {
            oppChoice = await ctx.Prompts.ChooseOption(1 - ctx.OwnerIndex,
                "对方亚马逊效果：是否将你生命区最上方 1 张卡牌放置到废弃区？",
                new[] { "放置到废弃区", "不放置（对方角色-2000）" });
        }
        else
        {
            // 对方没有生命牌可弃，视为"没有那样做"
            oppChoice = 1;
        }

        if (oppChoice == 0)
        {
            // 对方将其生命区最上方 1 张放置到废弃区
            var top = opp.LifeArea[0];
            opp.LifeArea.RemoveAt(0);
            opp.Trash.Add(top);
            return;
        }

        // 没有那样做：本回合中，对方最多 1 张领袖或角色力量-2000
        var cands = new List<CardInstance>();
        if (opp.Leader != null) cands.Add(opp.Leader);
        cands.AddRange(opp.Characters);
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "选择对方最多 1 张领袖或角色，本回合力量-2000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, -2000);
        }
    }
}
