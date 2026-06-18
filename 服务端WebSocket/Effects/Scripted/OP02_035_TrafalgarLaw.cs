using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-035 特拉法尔加·罗（角色）
/// 【启动主要】➀（可以将费用区中指定数量的咚‼转为休息状态），可以将此角色放回持有者的手牌中：
///   将我方手牌中最多 1 张费用为 3 的角色登场。
///
/// 实现说明 / 简化点：
///   - 括号内的「将 ➀ 咚转为休息状态」是可选附带成本（卡面以括号标注），按惯例只实现主收益分支。
///   - 主发动成本「将此角色放回手牌」用 BounceToHand(ctx.Source) 实现（不在 DSL cost 集合内，故用脚本）。
///   - 收益：从手牌登场最多 1 张费用为 3 的角色。
///   - 「可以」=可选，先 ConfirmOptional 询问。
/// </summary>
public class OP02_035_TrafalgarLaw : IScriptedEffect
{
    public string CardNumber => "OP02-035";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var playable = me.Hand.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost == 3).ToList();

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗【启动主要】：将此角色放回手牌，登场最多 1 张费用为 3 的角色？");
        if (!use) return;

        // 主成本：将此角色放回持有者手牌
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, self);

        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场最多 1 张费用为 3 的角色",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
