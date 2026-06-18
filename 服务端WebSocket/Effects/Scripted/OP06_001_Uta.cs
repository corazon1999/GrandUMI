using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-001 乌塔（领航 / 炎·暗 5000，FILM）
/// 【攻击时】可以丢弃我方手牌中 1 张拥有《FILM》特征的卡牌：
///   本回合中，对方最多 1 张角色力量 -2000。之后，从咚!!卡组中追加最多 1 张休息状态的咚!!。
///
/// 实现说明：
///   - 攻击声明时机（OnAttackDeclare）。"可以…：…"为可选效果，先 ConfirmOptional。
///   - 成本：丢弃手牌中 1 张《FILM》特征卡牌（脚本内可自由表达手牌弃牌成本）。
///   - 收益：对方最多 1 张角色 -2000（AddPowerThisTurn 负值）；之后从咚!!卡组追加 1 张休息状态咚。
/// </summary>
public class OP06_001_Uta : IScriptedEffect
{
    public string CardNumber => "OP06-001";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本要求：手牌中需有《FILM》特征卡牌
        var filmCards = me.Hand.Where(c => c.Info.HasKeyword("FILM")).ToList();
        if (filmCards.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "乌塔【攻击时】：丢弃手牌中 1 张《FILM》卡牌，使对方 1 张角色 -2000 并追加 1 张休息咚？");
        if (!use) return;

        // 成本：丢弃手牌中 1 张《FILM》特征卡牌
        var costExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = filmCards.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var discard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃手牌中 1 张《FILM》特征卡牌（成本）",
            filmCards.Select(c => c.Id.ToString()).ToList(), 1, 1, costExtra);
        if (discard.Count < 1) return; // 未支付成本
        var paid = filmCards.First(c => c.Id.ToString() == discard[0]);
        AtomicOps.DiscardHand(me, paid);

        // 收益 1：本回合中对方最多 1 张角色力量 -2000
        if (opp.Characters.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张对方角色，本回合力量 -2000",
                opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = opp.Characters.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddPowerThisTurn(tgt, -2000);
            }
        }

        // 收益 2：从咚!!卡组追加最多 1 张休息状态的咚!!
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }
}
