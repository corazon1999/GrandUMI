using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-092 希路麦波（角色，地，海军/利刃，6 费 7000）
/// 【登场时】可以丢弃我方的 1 张手牌：抽取 1 张卡牌，将我方废弃区中最多 1 张"希路麦波"以外的
///   费用不高于 8 且拥有《利刃》特征的角色卡牌登场。之后，当本回合结束时，将通过此效果登场的
///   1 张角色放回其持有者的卡组最下方。
///
/// 实现说明 / 简化点：
///   - 可选成本"丢弃我方 1 张手牌"用 ConfirmOptional + ChooseCards 选 1 张手牌弃掉。
///   - 收益：抽 1 张；再从废弃区选最多 1 张（非"希路麦波"、费用≤8、含《利刃》特征）的角色登场。
///   - "之后，当本回合结束时，将通过此效果登场的角色放回卡组最下方"为延迟到回合结束、且绑定到
///     新登场卡的一次性预约效果，引擎无延迟调度通道，故省略该回收段（仅实现核心收益），与
///     OP13-038/OP13-066 同类简化。
/// </summary>
public class OP11_092_Hiruluk : IScriptedEffect
{
    public string CardNumber => "OP11-092";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "希路麦波【登场时】：丢弃 1 张手牌，抽 1 张，并从废弃区登场 1 张《利刃》角色（费用≤8）？");
        if (!use) return;

        // 成本：丢弃我方 1 张手牌
        var discardPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方 1 张手牌作为成本",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (discardPick.Count < 1) return;
        var toDiscard = me.Hand.FirstOrDefault(c => c.Id.ToString() == discardPick[0]);
        if (toDiscard is null) return;
        AtomicOps.DiscardHand(me, toDiscard);

        // 收益 1：抽 1 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

        // 收益 2：从废弃区登场 1 张（非"希路麦波"、费用≤8、含《利刃》）的角色
        var cands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            !c.Info.Name.Contains("希路麦波") &&
            c.Info.Cost <= 8 &&
            c.Info.HasKeyword("利刃")
        ).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
            "从废弃区登场最多 1 张《利刃》角色（费用≤8）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
            if (picked is not null)
                AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
