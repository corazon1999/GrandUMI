using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-049 撒谎布（角色）
/// 【登场时】确认我方卡组最上方的 5 张卡牌，将其自选顺序排列并放置到卡组最上方或最下方。
///
/// 实现说明：
/// - 先让玩家二选一决定整体放"卡组最上方"还是"最下方"。
/// - 顺序：通过 ChooseCards 逐张让玩家从未排序卡中按期望顺序选出，得到自选顺序后用
///   ReorderTopK 一次性放置（toBottom=放底）。
/// </summary>
public class P_049_Usopp : IScriptedEffect
{
    public string CardNumber => "P-049";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        var me = ctx.State.Players[owner];

        int k = Math.Min(5, me.Deck.Count);
        if (k == 0) return;
        var top = me.Deck.Take(k).ToList();

        // 决定放顶还是放底
        int where = await ctx.Prompts.ChooseOption(owner,
            "确认卡组顶 5 张：放置到卡组最上方还是最下方？",
            new[] { "卡组最上方", "卡组最下方" });
        bool toBottom = where == 1;

        // 让玩家按期望顺序逐张选出（第一张为最先放置者）
        var remaining = top.ToList();
        var order = new List<Guid>();
        while (remaining.Count > 1)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = remaining.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var pick = await ctx.Prompts.ChooseCards(owner, "ReorderPick",
                toBottom ? "选择下一张放置到卡组最下方的卡（先选者更靠上）"
                         : "选择下一张放置到卡组最上方的卡（先选者位于最上方）",
                remaining.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
            var chosenId = pick.Count > 0 ? pick[0] : remaining[0].Id.ToString();
            var chosen = remaining.First(c => c.Id.ToString() == chosenId);
            order.Add(chosen.Id);
            remaining.Remove(chosen);
        }
        if (remaining.Count == 1) order.Add(remaining[0].Id);

        AtomicOps.ReorderTopK(me, order, toBottom);
    }
}
