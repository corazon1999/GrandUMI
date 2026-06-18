using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-046 萨波（角色）
/// 【阻挡者】（关键词，由引擎处理）
/// 【登场时】我方领袖拥有《德莱斯罗兹》特征的场合，
///   将我方手牌中最多 1 张拥有《德莱斯罗兹》特征的事件发动。
///
/// 实现说明：用 AtomicOps.PlayEventFromHandFree 从手牌免费发动所选事件（会触发其 EventMain 效果）。
/// </summary>
public class OP15_046_Sabo : IScriptedEffect
{
    public string CardNumber => "OP15-046";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 我方领袖须拥有《德莱斯罗兹》特征
        if (!me.Leader.Info.HasKeyword("德莱斯罗兹")) return;

        var events = me.Hand
            .Where(c => c.Info.Kind == CardKind.Event && c.Info.HasKeyword("德莱斯罗兹"))
            .ToList();
        if (events.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = events.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandEvent",
            "将手牌中最多 1 张《德莱斯罗兹》事件发动",
            events.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (pick.Count == 0) return;

        var chosen = events.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.PlayEventFromHandFree(ctx.State, ctx.OwnerIndex, chosen, ctx.Prompts);
    }
}
