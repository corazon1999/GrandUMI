using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-091 玛尔迦丽塔（1 费 0 力量，辛德丽影子的持有者）
/// 【登场时】将对方废弃区中的最多 1 张卡牌放回其持有者的卡组最下方。
///
/// 实现说明 / 简化点：
/// - 由我方选择对方废弃区中最多 1 张卡牌，放回对方卡组最下方（ReturnTrashToDeckBottom(opp, card)）。
///   候选为对方非公开/可见废弃区，通过 extra.choiceCards 展示卡面供选择。
/// - DSL 的 Choose 无 OpponentTrash 取值，故用脚本实现。
/// </summary>
public class OP15_091_Margaret : IScriptedEffect
{
    public string CardNumber => "OP15-091";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var trash = opp.Trash.ToList();
        if (trash.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = trash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentTrash",
            "将对方废弃区中的最多 1 张卡牌放回其卡组最下方",
            trash.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var card = trash.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.ReturnTrashToDeckBottom(opp, card);
        }
    }
}
