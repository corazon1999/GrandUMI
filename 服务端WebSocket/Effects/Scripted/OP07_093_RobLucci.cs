using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-093 罗布·鲁兹（角色 / CP0）
/// 【登场时】可以将我方废弃区中的 3 张卡牌自选顺序放回卡组最下方：
///   对方丢弃其 1 张手牌。之后，将对方废弃区中的最多 1 张卡牌放回卡组最下方。
///
/// 说明 / 简化点：
///   - 可选成本：废弃区需有 ≥3 张牌，选 3 张放回我方卡组最下方（按所选顺序放底）。
///   - 收益 1：对方丢弃 1 张手牌（由对手 Prompt 自选，经 AtomicOps.OpponentDiscardChosen，需 ctx.Engine）。
///   - 收益 2：将对方废弃区中最多 1 张卡牌放回（对方）卡组最下方。
/// </summary>
public class OP07_093_RobLucci : IScriptedEffect
{
    public string CardNumber => "OP07-093";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本前置条件：废弃区 ≥3 张
        if (me.Trash.Count < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗布·鲁兹【登场时】：将废弃区 3 张牌放回卡组最下方，使对方丢弃 1 张手牌，并将对方废弃区 1 张放回其卡组底？");
        if (!use) return;

        // 成本：选 3 张废弃区牌放回我方卡组最下方
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Trash
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var picked = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrashCard",
            "将废弃区 3 张牌自选顺序放回卡组最下方",
            me.Trash.Select(c => c.Id.ToString()).ToList(), 3, 3, extra);
        if (picked.Count < 3) return; // 成本未支付

        foreach (var cid in picked)
        {
            var card = me.Trash.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        // 收益 1：对方丢弃其 1 张手牌
        if (ctx.Engine is not null)
            await AtomicOps.OpponentDiscardChosen(ctx.Engine, 1 - ctx.OwnerIndex, 1);

        // 收益 2：将对方废弃区中最多 1 张卡牌放回（对方）卡组最下方
        if (opp.Trash.Count > 0)
        {
            var oppExtra = new Dictionary<string, object?>
            {
                ["choiceCards"] = opp.Trash
                    .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                    .ToList(),
            };
            var ch2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentTrashCard",
                "将对方废弃区中最多 1 张卡牌放回卡组最下方",
                opp.Trash.Select(c => c.Id.ToString()).ToList(), 0, 1, oppExtra);
            if (ch2.Count > 0)
            {
                var card = opp.Trash.First(c => c.Id.ToString() == ch2[0]);
                AtomicOps.ReturnTrashToDeckBottom(opp, card);
            }
        }
    }
}
