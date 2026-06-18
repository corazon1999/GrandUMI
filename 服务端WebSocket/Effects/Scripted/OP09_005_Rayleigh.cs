using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-005 希尔巴兹·雷利（角色）
/// 【阻挡者】（关键词，由引擎处理）
/// 【登场时】对方场上存在 2 张或更多原本的力量为 5000 或更高的角色的场合，
///   抽取 2 张卡牌，丢弃我方的 1 张手牌。
///
/// 说明 / 简化点：
/// - "原本的力量" 取卡面原始力量 c.Info.Power（不含修正）。
/// - 抽 2 后必须丢弃 1 张手牌（非可选）；通过 ChooseCards(min=1) 让玩家选择要丢的牌。
/// </summary>
public class OP09_005_Rayleigh : IScriptedEffect
{
    public string CardNumber => "OP09-005";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：对方场上原本力量 ≥5000 的角色 ≥2 张
        int big = opp.Characters.Count(c => c.Info.Power >= 5000);
        if (big < 2) return;

        // 抽 2
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);

        // 丢弃我方 1 张手牌（必须丢，若无手牌则跳过）
        if (me.Hand.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方的 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count > 0)
        {
            var card = me.Hand.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.DiscardHand(me, card);
        }
    }
}
