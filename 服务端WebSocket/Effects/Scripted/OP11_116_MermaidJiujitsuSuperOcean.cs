using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-116 人鱼柔术 超级海洋（事件）
/// 【主要】将最多 1 张费用不高于 6 的角色正面朝上加入其持有者生命区的最上方或最下方。
/// 【触发】将对方最多 1 张费用不高于 4 的角色正面朝上加入其持有者生命区的最上方或最下方。
///
/// 实现说明 / 简化点：
///   - 费用以当前费用（含持续费用修正）评估：ctx.State.CurrentCostOf(side, c)。
///   - 将场上角色移入生命区用 AtomicOps.MoveCharToLife（会先解除贴附的咚、清临时态）。
///     toTop=true 放最上方，toTop=false 放最下方，由玩家二选一决定。
///   - 【主要】候选为双方场上费用≤6 的角色（"其持有者生命区"=该角色所属方的生命区）；
///     【触发】仅限对方场上费用≤4 的角色。
///   - "正面朝上"为生命区牌的朝向标记，本引擎生命牌不区分朝向，按移入生命区处理。
/// </summary>
public class OP11_116_MermaidJiujitsuSuperOcean : IScriptedEffect
{
    public string CardNumber => "OP11-116";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        bool isTrigger = ctx.Trigger == EffectTrigger.OnLifeRevealTrigger;
        int costThreshold = isTrigger ? 4 : 6;

        // 收集候选：每个候选记录其所属方下标
        var cands = new List<(int side, CardInstance card)>();
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        foreach (var c in opp.Characters)
            if (ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= costThreshold)
                cands.Add((1 - ctx.OwnerIndex, c));

        if (!isTrigger)
        {
            // 【主要】也可选我方角色
            var me = ctx.State.Players[ctx.OwnerIndex];
            foreach (var c in me.Characters)
                if (ctx.State.CurrentCostOf(ctx.OwnerIndex, c) <= costThreshold)
                    cands.Add((ctx.OwnerIndex, c));
        }

        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Character",
            isTrigger
                ? "选择对方最多 1 张费用≤4 的角色加入其持有者生命区"
                : "选择最多 1 张费用≤6 的角色加入其持有者生命区",
            cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);

        // 选择放生命区最上方或最下方
        int pos = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将该角色加入生命区的位置", new[] { "最上方", "最下方" });

        AtomicOps.MoveCharToLife(ctx.State, picked.side, picked.card, toTop: pos == 0);
    }
}
