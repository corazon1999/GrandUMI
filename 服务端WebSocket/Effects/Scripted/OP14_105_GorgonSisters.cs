using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-105 戈耳工三姐妹（角色）
/// 【启动主要】【每回合1次】可以公开我方手牌中 3 张拥有《亚马逊·百合》或《九蛇海盗团》特征的卡牌：
///   赋予我方所有领袖和角色各最多 1 张休息状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 成本"公开 3 张特定特征手牌"用 ChooseCards 选择 3 张表示公开（仍留在手牌）。
///   - "赋予所有领袖和角色各 1 张咚!!"对每个目标各调用一次 AttachDonFromCost；
///     从费用区取活跃咚附着（引擎将其置为 Attached）。每个目标至多 1 张，受费用区剩余咚限制。
///   - 触发节"我方领袖拥有《九蛇海盗团》特征则登场"由 DSL/触发系统处理，此处仅实现启动主要主体。
/// </summary>
public class OP14_105_GorgonSisters : IScriptedEffect
{
    public string CardNumber => "OP14-105";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 每回合 1 次
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本候选：手牌中拥有《亚马逊·百合》或《九蛇海盗团》特征的卡牌
        var cands = me.Hand.Where(c =>
            c.Info.HasKeyword("亚马逊·百合") || c.Info.HasKeyword("九蛇海盗团")).ToList();
        if (cands.Count < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "戈耳工三姐妹【启动主要】：公开手牌中 3 张《亚马逊·百合》/《九蛇海盗团》，赋予我方所有领袖与角色各 1 张休息咚？");
        if (!use) return;

        // 成本：公开（选择）3 张
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var revealed = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RevealCards",
            "公开手牌中的 3 张《亚马逊·百合》/《九蛇海盗团》",
            cands.Select(c => c.Id.ToString()).ToList(), 3, 3, extra);
        if (revealed.Count < 3) return; // 成本未支付

        // 向对手公开所选 3 张《亚马逊·百合》/《九蛇海盗团》：广播牌面浮层（公开≠丢弃，仍留手牌）
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex,
            revealed.Select(id => cands.First(c => c.Id.ToString() == id).Info.Number).ToList());

        me.TurnOnceUsed.Add(key);

        // 赋予所有领袖与角色各最多 1 张「休息状态」的咚!!（DonState.Rest；只取费用区休息咚，
        // 无休息咚则不赋予，不回退活跃咚，见 AttachDonFromCost 与 ST17-004 修复记录）
        AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);
        foreach (var ch in me.Characters.ToList())
        {
            AtomicOps.AttachDonFromCost(me, ch.Id, 1, DonState.Rest);
        }
    }
}
