using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-014 甚平（角色）
/// 【阻挡者】（关键词由引擎处理）
/// 【咚!!×1】【阻挡时】将我方手牌中最多1张费用不高于2的红色角色卡牌登场。
///
/// 实现说明：
///   - 关键词【阻挡者】由引擎处理，本脚本只实现【阻挡时】主动效果。
///   - 【咚!!×1】= 自身被赋予咚≥1；红色 = ColorList.Contains("红")；费用≤2。
/// </summary>
public class OP01_014_Jinbe : IScriptedEffect
{
    public string CardNumber => "OP01-014";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnBlockDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】
        if (me.AttachedDonCount(self.Id) < 1) return;

        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 2 &&
            c.Info.ColorList.Contains("红")
        ).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多1张费用≤2的红色角色登场",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
