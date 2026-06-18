using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-095 林德巴古
/// 我方领袖拥有《革命军》特征的场合，此角色的费用+4。（被动）
/// 【登场时】抽取 1 张卡牌，丢弃我方的 1 张手牌。
///
/// 简化点：
/// - 卡面常驻被动"我方领袖拥有《革命军》特征的场合，此角色的费用+4"未实现
///   （引擎的条件式静态费用修正缺口，与 OP12-042/OP12-043 同类）。本脚本只实现【登场时】。
/// - 【登场时】抽 1 丢 1 为强制效果：先抽 1，再让玩家从手牌选 1 张丢弃；
///   弃牌经 extra.choiceCards 下发卡面给客户端。
/// </summary>
public class OP12_095_Lindbergh : IScriptedEffect
{
    public string CardNumber => "OP12-095";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        // 抽 1 张
        AtomicOps.Draw(s, ctx.OwnerIndex, 1);

        // 丢弃我方 1 张手牌（强制；手牌为空则无可丢）
        if (me.Hand.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Lindbergh095Discard",
            "丢弃 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        var discard = chosen.Count > 0
            ? me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0])
            : me.Hand[0];
        if (discard is null) return;
        AtomicOps.DiscardHand(me, discard);
    }
}
