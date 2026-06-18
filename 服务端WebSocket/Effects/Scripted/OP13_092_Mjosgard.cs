using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-092 缪斯加鲁德圣（2 费 0 力 地 / 天龙人）
/// 【登场时】我方生命卡牌不多于 3 张的场合，将我方废弃区中最多 1 张
///   费用为 1 且拥有《圣地玛丽乔尔》特征的舞台卡牌登场。
///
/// 实现说明：
///   - 条件：我方生命 ≤ 3。
///   - "最多 1 张"= 可选（min=0）。
///   - 候选限定为废弃区中 费用==1、Kind==Stage、含《圣地玛丽乔尔》特征 的卡牌
///     （DSL 的 OwnTrash 候选无法按这些条件过滤，故用脚本）。
///   - 废弃区为私有区域，需经 extra.choiceCards 让前端显示卡面。
///   - 登场使用 AtomicOps.PlayFromTrashFree（无费用，活跃状态）。
/// </summary>
public class OP13_092_Mjosgard : IScriptedEffect
{
    public string CardNumber => "OP13-092";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 我方生命卡牌不多于 3 张
        if (me.LifeCount > 3) return;

        var candidates = me.Trash
            .Where(c => c.Info.Kind == CardKind.Stage
                        && c.Info.Cost == 1
                        && c.Info.HasKeyword("圣地玛丽乔尔"))
            .ToList();
        if (candidates.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrash",
            "将废弃区中最多 1 张费用 1 且《圣地玛丽乔尔》舞台登场",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);

        if (chosen.Count > 0)
        {
            var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
            if (target is not null) AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, target);
        }
    }
}
