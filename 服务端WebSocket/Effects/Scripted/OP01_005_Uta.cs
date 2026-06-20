using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-005 乌塔（角色）
/// 【登场时】将我方废弃区中最多1张"乌塔"以外且费用不高于3的红色角色卡牌加入手牌。
///
/// 实现说明：
///   - 红色 = 颜色集合含"红"(ColorList.Contains("红"))。
///   - "乌塔"以外用 !MatchesName("乌塔")；费用≤3、角色类型。
///   - 废弃区为可见区，加入手牌用 TrashToHand。
/// </summary>
public class OP01_005_Uta : IScriptedEffect
{
    public string CardNumber => "OP01-005";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 3 &&
            c.Info.ColorList.Contains("红") &&
            !c.MatchesName("乌塔")
        ).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCharacter",
            "将废弃区中最多1张『乌塔』以外、费用≤3的红色角色加入手牌",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.TrashToHand(me, picked);
        }
    }
}
