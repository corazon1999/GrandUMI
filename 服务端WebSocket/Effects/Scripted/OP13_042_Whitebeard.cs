using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-042 爱德华·纽哥特（10 费 12000 水）
/// 【阻挡者】【登场时】抽取 2 张卡牌，丢弃我方的 1 张手牌。
///   之后，赋予我方领袖和 1 张角色各最多 2 张休息状态的咚!!。
///
/// 实现说明：
///   - 【阻挡者】为静态关键字，由 ActionValidator.HasKeyword 直接读取效果文本识别，无需脚本处理。
///   - 本脚本实现【登场时】：抽 2 张 → 丢弃我方 1 张手牌（玩家自选，经 extra.choiceCards 显示卡面）
///     → 赋予我方领袖最多 2 张休息状态的咚!! → 选 1 张我方角色赋予最多 2 张休息状态的咚!!。
///   - "赋予休息状态的咚"沿用本仓库约定：AttachDonFromCost(..., DonState.Rest)，
///     即从费用区休息状态的咚中取最多 N 张赋予目标（咚数量不足时尽量赋予）。
/// </summary>
public class OP13_042_Whitebeard : IScriptedEffect
{
    public string CardNumber => "OP13-042";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 抽取 2 张
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        // 丢弃我方 1 张手牌（强制，手牌非空时）
        if (me.Hand.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var discardChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "丢弃 1 张手牌",
                me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
            if (discardChosen.Count > 0)
            {
                var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == discardChosen[0]);
                if (card is not null) AtomicOps.DiscardHand(me, card);
            }
            else
            {
                AtomicOps.DiscardHand(me, me.Hand[0]);
            }
        }

        // 赋予我方领袖最多 2 张休息状态的咚!!
        AtomicOps.AttachDonFromCost(me, me.Leader.Id, 2, DonState.Rest);

        // 选 1 张我方角色，赋予最多 2 张休息状态的咚!!
        if (me.Characters.Count > 0)
        {
            var charChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "选择 1 张我方角色，赋予最多 2 张休息状态的咚!!",
                me.Characters.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (charChosen.Count > 0)
            {
                var target = me.Characters.FirstOrDefault(c => c.Id.ToString() == charChosen[0]);
                if (target is not null)
                    AtomicOps.AttachDonFromCost(me, target.Id, 2, DonState.Rest);
            }
        }
    }
}
