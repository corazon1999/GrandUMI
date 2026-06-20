using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-062 紫罗兰（角色）
/// 【阻挡者】（关键词，引擎处理）
/// 【KO时】咚!!-1：我方领袖拥有《堂吉诃德海盗团》特征的场合，
///   将我方废弃区中最多 1 张紫色事件加入手牌。
///
/// 实现说明：
/// - 成本『咚!!-1』在 KO 时触发节无法用 DSL 表达（triggers 不支持 cost），故用脚本。
/// - 仅当领袖含《堂吉诃德海盗团》且费用区有可放回的咚时本效果才有收益，故先校验再以
///   ConfirmOptional 询问是否支付成本发动。
/// - 紫色事件以元素色"紫"与卡种 Event 判定。
/// </summary>
public class OP10_062_Viola : IScriptedEffect
{
    public string CardNumber => "OP10-062";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 前置条件：领袖拥有《堂吉诃德海盗团》特征
        if (!me.Leader.Info.HasKeyword("堂吉诃德海盗团")) return;

        // 收益候选：废弃区中的紫色事件
        var cands = me.Trash
            .Where(c => c.Info.Kind == CardKind.Event && c.Info.ColorList.Contains("紫"))
            .ToList();
        if (cands.Count == 0) return;

        // 成本可支付性：费用区需有 ≥1 张可放回的咚
        bool canPayCost = me.CostArea.Count >= 1;
        if (!canPayCost) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "紫罗兰【KO时】：支付『咚!!-1』，将废弃区中最多 1 张紫色事件加入手牌？");
        if (!use) return;

        // 成本：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return; // 成本未实际支付

        // 收益：废弃区紫色事件最多 1 张加入手牌
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashEvent",
            "将废弃区中最多 1 张紫色事件加入手牌",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.TrashToHand(me, picked);
        }
    }
}
