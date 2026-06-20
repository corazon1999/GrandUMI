using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-123 夏洛特·卡塔库栗（角色）
/// 【登场时】将最多 1 张费用不高于 8 的角色正面朝上加入其持有者生命区的最上方或最下方。
///
/// 实现说明 / 简化点：
/// - 目标可为任意一方场上、费用≤8 的角色（"其持有者"=该角色所属玩家，故置入的是其所属方的生命区）。
/// - 费用判定用 GameState.CurrentCostOf(sideIdx, card)（含持续费用修正）。
/// - 用 ChooseOption 让玩家选择放入生命区"最上方/最下方"，对应 MoveCharToLife 的 toTop 参数。
/// - 生命牌默认即正面/背面由展示层处理；此处置入生命区即满足"加入生命区"。
/// </summary>
public class OP03_123_Katakuri : IScriptedEffect
{
    public string CardNumber => "OP03-123";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        // 收集两方场上费用≤8 的角色作为候选（记录其所属方下标）
        var cands = new List<(CardInstance card, int ownerIdx)>();
        for (int side = 0; side < 2; side++)
        {
            foreach (var c in ctx.State.Players[side].Characters)
            {
                if (ctx.State.CurrentCostOf(side, c) <= 8)
                    cands.Add((c, side));
            }
        }
        if (cands.Count == 0) return;

        // 询问是否发动（"最多1张"=可选）
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卡塔库栗【登场时】：将最多 1 张费用≤8 的角色正面朝上加入其持有者生命区？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(t => new { id = t.card.Id.ToString(), number = t.card.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "选择最多 1 张费用≤8 的角色加入其持有者生命区",
            cands.Select(t => t.card.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = cands.First(t => t.card.Id.ToString() == chosen[0]);

        // 选择放入生命区最上方 / 最下方
        int pos = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将该角色加入生命区的位置", new[] { "最上方", "最下方" });
        bool toTop = pos == 0;

        AtomicOps.MoveCharToLife(ctx.State, picked.ownerIdx, picked.card, toTop);
    }
}
