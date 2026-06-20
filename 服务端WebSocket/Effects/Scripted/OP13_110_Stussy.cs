using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-110 斯图西（光 / 艾格赫德·CP0 / 7 费 7000）
///
/// 完整文本：
///   【阻挡者】
///   【登场时】我方领袖拥有《艾格赫德》特征的场合，将我方手牌中最多 1 张费用不高于 5
///            且拥有【触发】效果的角色卡牌登场。
///
/// 本脚本实现【登场时】部分：
///   - 条件：我方领袖拥有《艾格赫德》特征。
///   - 候选：手牌中费用 ≤5、拥有【触发】效果（Info.Trigger 非空）的角色卡；最多 1 张（min=0 可跳过）。
///   - 以 PlayFromHandFree 免费登场。
///   - 手牌候选经 extra.choiceCards 下发卡面给客户端。
///
/// 简化点：
///   - 【阻挡者】为静态关键字，由引擎按卡面关键字处理，脚本不重复实现。
///   - "拥有【触发】效果"判定为 CardInfo.Trigger 字段非空。
/// </summary>
public class OP13_110_Stussy : IScriptedEffect
{
    public string CardNumber => "OP13-110";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方领袖拥有《艾格赫德》特征
        if (!me.Leader.Info.HasKeyword("艾格赫德")) return;

        // 候选：手牌中费用 ≤5 且拥有【触发】效果的角色卡
        var candidates = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character
                        && c.Info.Cost <= 5
                        && !string.IsNullOrEmpty(c.Info.Trigger))
            .ToList();
        if (candidates.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多 1 张费用不高于 5 且拥有【触发】效果的角色登场",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var card = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, card);
    }
}
