using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-044 战国（角色 / 地 / 海军，cost7 power7000）
/// 【阻挡者】（关键词，引擎处理）
/// 【登场时】将我方废弃区中最多 1 张费用不高于 4 且拥有《海军》特征的黑色角色卡牌以休息状态登场。
///
/// 实现说明：
///   - 黑色 = 颜色集合含 "黑"（ColorList.Contains("黑")）。
///   - 候选为废弃区中 角色 且 费用≤4 且 含《海军》 且 黑色。
///   - 选定后用 PlayFromTrashFree(restState:true) 以休息状态登场。
/// </summary>
public class EB02_044_Sengoku : IScriptedEffect
{
    public string CardNumber => "EB02-044";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 4 &&
            c.Info.HasKeyword("海军") &&
            c.Info.ColorList.Contains("黑")
        ).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
            "将废弃区中最多 1 张费用≤4 的黑色《海军》角色以休息状态登场",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var picked = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked, restState: true);
    }
}
