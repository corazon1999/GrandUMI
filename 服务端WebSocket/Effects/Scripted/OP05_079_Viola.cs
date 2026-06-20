using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-079 维奥拉（角色，地）
/// 【登场时】对方将其废弃区中的 3 张卡牌自选顺序放回其卡组最下方。
///
/// 实现说明 / 简化点：
///   - 由对方（1-OwnerIndex）从自己废弃区中选择 3 张放回其卡组最下方（ReturnTrashToDeckBottom(opp, card)）。
///   - 操作的是对方废弃区，prompt 传 choiceCards 以展示卡面。
///   - “自选顺序”实现为按对方选择的先后顺序依次放到卡组最下方。
///   - 废弃区不足 3 张时，按实际数量全部放回。
/// </summary>
public class OP05_079_Viola : IScriptedEffect
{
    public string CardNumber => "OP05-079";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        var trash = opp.Trash.ToList();
        if (trash.Count == 0) return;

        int need = Math.Min(3, trash.Count);

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = trash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(oppIdx, "OwnTrash",
            "将你废弃区中的 " + need + " 张卡牌自选顺序放回卡组最下方",
            trash.Select(c => c.Id.ToString()).ToList(), need, need, extra);

        foreach (var id in chosen)
        {
            var card = trash.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(opp, card);
        }
    }
}
