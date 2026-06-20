using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-035 罗罗诺亚·佐罗（角色 / 绿 / 草帽一伙，7 费）
/// 【登场时】将对方最多1张卡牌转为休息状态。之后，可以丢弃我方的1张手牌。
///   那样做的场合，赋予我方领袖最多3张休息状态的咚!!。
///
/// 实现（原 DSL 版只做了第1段且只列对方领袖+角色、漏咚/舞台，第2段整个忽略；改脚本补全）：
///   - 第1段：AtomicOps.PromptRestOpponentCards(ctx,1)——对方活跃 领袖/角色/舞台/咚!! 中选最多1张休置。
///   - 第2段：可选弃1张手牌 → AttachDonFromCost(rest,3) 赋予领袖最多3张休息咚
///     （"赋予领袖休息咚"= 从费用区取休息咚附给领袖，同 EB02-006/EB02-011/OP11-095 约定）。
/// </summary>
public class OP16_035_Zoro : IScriptedEffect
{
    public string CardNumber => "OP16-035";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 1) 将对方最多 1 张卡牌（活跃 领袖/角色/舞台/咚!!）转为休息状态
        await AtomicOps.PromptRestOpponentCards(ctx, 1);

        // 2) 可以丢弃我方 1 张手牌；那样做则赋予领袖最多 3 张休息咚
        if (me.Hand.Count == 0) return;
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "佐罗【登场时】：丢弃我方 1 张手牌，赋予我方领袖最多 3 张休息状态的咚!!？");
        if (!use) return;

        var hand = me.Hand.ToList();
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen",
            "选择 1 张手牌丢弃", hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (pick.Count < 1) return;
        var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == pick[0]);
        if (card is null) return;

        AtomicOps.DiscardHand(me, card);
        AtomicOps.AttachDonFromCost(me, me.Leader.Id, 3, DonState.Rest);
    }
}
