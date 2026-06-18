using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-104 光月日和（角色 / 光 / 4 费 / 0 / 和之国・光月家）
/// 【阻挡者】（卡数据静态声明，引擎按关键字处理，脚本不涉及）
/// 【KO时】可以丢弃我方的 1 张手牌：我方领袖为多种颜色的场合，
///   将我方卡组最上方的最多 1 张卡牌加入生命区最上方。
///
/// 说明：
///   - 可选成本“可以丢弃我方 1 张手牌”：先 ConfirmOptional，再选 1 张手牌丢弃。
///     手牌为空则无法支付，效果不发动。
///   - “多种颜色” = 领袖颜色数 ≥ 2（ColorList.Length >= 2）。
///   - “最多 1 张加入生命区最上方” = AddLifeFromDeckTop(me, 1)。卡组顶不可见，为纯收益，自动加入 1 张。
///     成本（丢手牌）已支付但领袖非多色时不补回手牌（忠实原文：成本独立于条件）。
/// </summary>
public class OP13_104_Hiyori : IScriptedEffect
{
    public string CardNumber => "OP13-104";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        // 成本：可以丢弃 1 张手牌。手牌为空则无法支付
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "日和【KO时】：丢弃 1 张手牌，若领袖为多种颜色则将卡组顶 1 张加入生命区最上方？");
        if (!use) return;

        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var dchosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Hiyori104Discard",
            "丢弃 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
        var discard = dchosen.Count > 0
            ? me.Hand.FirstOrDefault(c => c.Id.ToString() == dchosen[0])
            : me.Hand[0];
        if (discard is null) return;
        AtomicOps.DiscardHand(me, discard);

        // 条件：我方领袖为多种颜色
        if (me.Leader.Info.ColorList.Length < 2) return;

        // 将我方卡组最上方的最多 1 张卡牌加入生命区最上方
        AtomicOps.AddLifeFromDeckTop(me, 1);
    }
}
