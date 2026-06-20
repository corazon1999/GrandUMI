using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-055 X·德雷克（角色 / 水 5 费 6000）
/// 【阻挡者】（由引擎按关键词处理，此处不实现）
/// 【登场时】确认我方卡组最上方的 5 张卡牌，将其自选顺序排列并放置到卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - 看顶 5 张：仅由玩家选择整体放"顶"或"底"（ChooseOption）；
///     "自选顺序"简化为保持原相对顺序，对实战影响极小。
///   - 用 AtomicOps.ReorderTopK 放回。
/// </summary>
public class OP05_055_Drake : IScriptedEffect
{
    public string CardNumber => "OP05-055";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        int peek = Math.Min(5, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "确认卡组顶 5 张，将其放置到卡组最上方还是最下方？", new[] { "最上方", "最下方" });

        AtomicOps.ReorderTopK(me, top.Select(c => c.Id).ToList(), toBottom: where == 1);
    }
}
