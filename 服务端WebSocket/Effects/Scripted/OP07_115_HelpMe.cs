using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-115 救救我呃啊啊—— !!!（事件 / 光 1 费）
/// 【反击】我方生命卡牌不多于2张的场合，本次战斗中，我方最多1张领袖或角色力量+3000。
/// 【触发】将我方废弃区中最多1张费用不高于5且拥有《艾格赫德》特征的角色卡牌登场。
///
/// 实现说明 / 简化点：
///   - 【反击】被"我方生命≤2"门控：DSL 的 counter 节无法表达条件，故用脚本 if 判定生命数后再给力量。
///     满足条件时让玩家选最多1张领袖或角色本次战斗+3000；不满足则无收益。
///   - 【触发】(OnLifeRevealTrigger)：从废弃区公开最多1张费用≤5且《艾格赫德》角色免费登场。
///   - 废弃区为可见区，登场目标用 choiceCards 下发卡面。
/// </summary>
public class OP07_115_HelpMe : IScriptedEffect
{
    public string CardNumber => "OP07-115";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventCounter)
            await ResolveCounter(ctx);
        else if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
            await ResolveTrigger(ctx);
    }

    // 【反击】我方生命≤2 时，最多1张领袖或角色本次战斗+3000
    private static async Task ResolveCounter(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 生命条件门控
        if (me.LifeCount > 2) return;

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多1张领袖或角色，本次战斗力量+3000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 3000);
        }
    }

    // 【触发】废弃区最多1张费用≤5的《艾格赫德》角色登场
    private static async Task ResolveTrigger(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 5 &&
            c.Info.HasKeyword("艾格赫德")
        ).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
            "将废弃区最多1张费用≤5的《艾格赫德》角色登场",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
