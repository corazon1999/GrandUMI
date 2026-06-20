using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-063 Mr.1(达兹·波涅斯)（角色 / 水 cost1）
/// 【登场时】将我方废弃区中最多 1 张费用为 1 的蓝色事件加入手牌。
///
/// 实现说明：
///   - 候选为废弃区中 费用为 1、蓝色(水)、事件 的卡，选 0..1 张加入手牌。
///   - 颜色用 ColorList.Contains("蓝") 判定。
/// </summary>
public class OP02_063_MisterOne : IScriptedEffect
{
    public string CardNumber => "OP02-063";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Event &&
            c.Info.Cost == 1 &&
            c.Info.ColorList.Contains("蓝")).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCard",
            "将废弃区中最多 1 张 费用为 1 的蓝色事件加入手牌",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.TrashToHand(me, picked);
        }
    }
}
