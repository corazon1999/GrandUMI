using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-070 蒙奇·D·路飞（角色）
/// 【登场时】咚!!-1（可以将我方场上 1 张咚!! 放回咚!!卡组），可以从手牌丢弃 1 张费用为 5 的角色卡牌：
///   本回合中，此角色获得【速攻】效果。
///
/// 说明：咚!!-1 与"丢弃 1 张费用 5 角色"为两段可选成本，二者均支付后获得【速攻】。
/// 用 ConfirmOptional 询问是否发动；确认后需场上≥1咚且手牌含 1 张费用 5 角色才可支付。
/// 【速攻】为本回合临时获得，用 GiveKeyword(ThisTurn)。
/// </summary>
public class OP03_070_Luffy : IScriptedEffect
{
    public string CardNumber => "OP03-070";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 成本前提：场上≥1咚 且 手牌中有费用 5 的角色
        if (me.CostArea.Count < 1) return;
        var cost5Chars = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost == 5)
            .ToList();
        if (cost5Chars.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞【登场时】：咚!!-1 并丢弃 1 张费用 5 的角色，本回合此角色获得【速攻】？");
        if (!use) return;

        // 选择要丢弃的费用 5 角色
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cost5Chars.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var discard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "丢弃 1 张费用 5 的角色卡牌",
            cost5Chars.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (discard.Count < 1) return;

        // 支付成本：咚!!-1 + 丢弃所选角色
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
        var card = cost5Chars.First(c => c.Id.ToString() == discard[0]);
        AtomicOps.DiscardHand(me, card);

        // 效果：本回合获得【速攻】
        AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.ThisTurn);
    }
}
