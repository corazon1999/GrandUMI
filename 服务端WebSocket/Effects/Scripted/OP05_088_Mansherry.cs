using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-088 蔓谢丽（角色）
/// 【启动主要】①（可以将费用区中指定数量的咚!!转为休息状态），可以将此角色转为休息状态，
///   并将我方废弃区中的 2 张卡牌自选顺序放回卡组最下方：
///   将我方废弃区中最多 1 张费用为 3 到 5 的黑色角色卡牌加入手牌。
///
/// 实现说明 / 简化点：
///   - 标头 ①（横置 1 张咚的成本）按惯例不在脚本内强制扣除（触发/启动成本由引擎层处理），
///     此处只实现"横置自身 + 废弃区 2 张放回卡组底"的支付与收益。
///   - "可以"=可选发动；废弃区不足 2 张无法支付成本。
///   - "自选顺序"由玩家选择 2 张废弃区卡牌，按选择顺序放回卡组最下方。
///   - 收益：从废弃区取最多 1 张费用 3-5 的黑色角色加入手牌。
/// </summary>
public class OP05_088_Mansherry : IScriptedEffect
{
    public string CardNumber => "OP05-088";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (me.Trash.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "蔓谢丽【启动主要】：横置此角色并将废弃区 2 张放回卡组底，将废弃区最多 1 张费用3-5 的黑色角色加入手牌？");
        if (!use) return;

        // 成本：横置自身
        AtomicOps.RestCard(self);

        // 成本：选择废弃区 2 张，按选择顺序放回卡组最下方
        var trashExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Trash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var picks = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashToDeckBottom",
            "选择废弃区 2 张，按选择顺序放回卡组最下方",
            me.Trash.Select(c => c.Id.ToString()).ToList(), 2, 2, trashExtra);
        foreach (var id in picks)
        {
            var c = me.Trash.FirstOrDefault(x => x.Id.ToString() == id);
            if (c != null) AtomicOps.ReturnTrashToDeckBottom(me, c);
        }

        // 收益：废弃区中最多 1 张费用 3-5 的黑色角色加入手牌
        var cand = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost >= 3 && c.Info.Cost <= 5 &&
            c.Info.ColorList.Contains("黑")
        ).ToList();
        if (cand.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
            "将废弃区最多 1 张费用 3-5 的黑色角色加入手牌",
            cand.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cand.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.TrashToHand(me, picked);
        }
    }
}
