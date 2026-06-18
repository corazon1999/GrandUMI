using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-045 特拉法尔加·罗（角色 / 地 / 德莱斯罗兹・超新星・心脏海盗团，cost5 power6000）
/// 【阻挡者】（关键词，引擎处理）
/// 【登场时】可以将我方废弃区中的 2 张卡牌自选顺序放回卡组最下方：选择以下的 1 项。
///   ・抽取 1 张卡牌。
///   ・对方持有 5 张或更多手牌的场合，对方丢弃其 1 张手牌。
///
/// 实现说明：
///   - 成本（可选）：废弃区≥2 张时，选 2 张放回卡组最下方（自选顺序简化为选定顺序放底）。
///   - 支付后用 ChooseOption 二选一；第二项需对方手牌≥5 才有效，否则回退为抽 1。
/// </summary>
public class EB02_045_Law : IScriptedEffect
{
    public string CardNumber => "EB02-045";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.Trash.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "特拉法尔加·罗【登场时】：将废弃区 2 张卡牌放回卡组最下方，然后二选一（抽 1 / 对方手牌≥5 时令其弃 1）？");
        if (!use) return;

        // 成本：选废弃区 2 张放回卡组最下方
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Trash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashToDeck",
            "选择废弃区 2 张卡牌放回卡组最下方",
            me.Trash.Select(c => c.Id.ToString()).ToList(), 2, 2, extra);
        if (pick.Count < 2) return; // 未完成成本

        foreach (var id in pick)
        {
            var c = me.Trash.FirstOrDefault(x => x.Id.ToString() == id);
            if (c is not null) AtomicOps.ReturnTrashToDeckBottom(me, c);
        }

        // 效果：二选一
        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择其一",
            new[] { "抽取 1 张卡牌", "对方手牌≥5 时令其丢弃 1 张手牌" });

        if (opt == 1 && opp.Hand.Count >= 5 && ctx.Engine is { } eng)
        {
            await AtomicOps.OpponentDiscardChosen(eng, 1 - ctx.OwnerIndex, 1);
        }
        else
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        }
    }
}
