using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-052 波尔·玛丽格鲁德（角色）
/// 【阻挡者】（由 ActionValidator 依效果文本自动识别，无需脚本处理）
/// 【登场时】我方领袖为“波尔·汉库珂”的场合，将我方手牌中最多 1 张费用不高于 6 的
///   “波尔·汉库珂”登场。
///
/// 说明：“最多 1 张” → 可选（min=0）。从手牌登场身份对客户端不可见，需传 extra.choiceCards 显示卡面。
/// </summary>
public class OP13_052_Marigold : IScriptedEffect
{
    public string CardNumber => "OP13-052";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 条件：我方领袖为“波尔·汉库珂”
        if (!me.Leader.Info.NameIs("波尔·汉库珂")) return;

        // 候选：手牌中费用不高于 6 的“波尔·汉库珂”角色
        var candidates = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character
                        && c.Info.Name == "波尔·汉库珂"
                        && c.Info.Cost <= 6)
            .ToList();
        if (candidates.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandHancock",
            "将最多 1 张费用不高于 6 的“波尔·汉库珂”从手牌登场（可不选）",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var pick = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, pick);
    }
}
