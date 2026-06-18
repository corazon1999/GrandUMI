using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-026 波尔·汉库珂（角色 / 水 / 王下七武海・九蛇海盗团，6 费 8000）
/// 【登场时】对方持有 5 张或更多手牌的场合，对方将其 1 张手牌放回卡组最下方。
/// 【启动主要】【每回合1次】可以将我方 1 张角色放回其持有者的卡组最下方：
///   赋予我方领袖和 1 张角色各最多 1 张休息状态的咚!!。
///
/// 实现说明：
///   - 登场段：对方手牌 ≥5 时由对方(1-owner)自选 1 张手牌放回卡组最下方(ReturnHandToDeckBottom)。
///   - 启动主要段：每回合1次，成本为"将我方 1 张角色放回卡组最下方"(ReturnFieldToDeckBottom)；
///     之后给领袖赋予最多 1 张休息咚、再给 1 张角色赋予最多 1 张休息咚(AttachDonFromCost)。
/// </summary>
public class EB03_026_BoaHancock : IScriptedEffect
{
    public string CardNumber => "EB03-026";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (opp.Hand.Count < 5) return;

            var oppHand = opp.Hand.ToList();
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = oppHand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(1 - ctx.OwnerIndex, "OppHandToDeckBottom",
                "将你的 1 张手牌放回卡组最下方",
                oppHand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
            if (chosen.Count > 0)
            {
                var card = opp.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
                if (card is not null) AtomicOps.ReturnHandToDeckBottom(opp, card);
            }
            return;
        }

        // 【启动主要】【每回合1次】
        var key = CardNumber + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：将我方 1 张角色放回其持有者的卡组最下方（"可以…"=可选）
        var chars = me.Characters.ToList();
        if (chars.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "波尔·汉库珂【启动主要】：将我方 1 张角色放回卡组最下方，赋予领袖和 1 张角色各最多 1 张休息咚?");
        if (!use) return;

        var pickRet = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将 1 张我方角色放回卡组最下方",
            chars.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pickRet.Count == 0) return;

        var retCard = chars.FirstOrDefault(c => c.Id.ToString() == pickRet[0]);
        if (retCard is null) return;
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, retCard);

        me.TurnOnceUsed.Add(key);

        // 收益：赋予我方领袖最多 1 张休息状态的咚!!
        AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);

        // 再赋予我方 1 张角色最多 1 张休息状态的咚!!
        var donChars = me.Characters.ToList();
        if (donChars.Count > 0)
        {
            var pickTgt = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "选择 1 张角色，赋予最多 1 张休息状态的咚!!",
                donChars.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pickTgt.Count > 0)
            {
                var tgt = donChars.First(c => c.Id.ToString() == pickTgt[0]);
                AtomicOps.AttachDonFromCost(me, tgt.Id, 1, DonState.Rest);
            }
        }
    }
}
