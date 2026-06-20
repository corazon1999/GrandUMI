using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-091 贝里古德（角色，地）
/// 【登场时】对方将其废弃区中的 3 张事件自选顺序放回其卡组最下方。
///
/// 实现说明 / 简化点：
///   - 由对方（1-OwnerIndex）从自己废弃区的事件中选择 3 张放回卡组最下方。
///   - 操作的是对方非公开/可见的废弃区，prompt 传 choiceCards 以展示卡面。
///   - “自选顺序”实现为按对方选择的先后顺序依次放到卡组最下方。
///   - 废弃区事件不足 3 张时，按实际数量全部放回。
/// </summary>
public class OP11_091_Verigood : IScriptedEffect
{
    public string CardNumber => "OP11-091";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        var events = opp.Trash.Where(c => c.Info.Kind == CardKind.Event).ToList();
        if (events.Count == 0) return;

        int need = Math.Min(3, events.Count);

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = events.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(oppIdx, "OwnTrash",
            "将你废弃区中的 " + need + " 张事件自选顺序放回卡组最下方",
            events.Select(c => c.Id.ToString()).ToList(), need, need, extra);

        foreach (var id in chosen)
        {
            var card = events.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(opp, card);
        }
    }
}
