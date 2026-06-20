using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-098 阿户子（角色 / 光 / 和之国）
/// 【登场时】可以丢弃我方手牌中 2 张拥有《和之国》特征的卡牌：
///   我方生命卡牌不多于 1 张的场合，将我方卡组最上方的 1 张卡牌加入生命区最上方。
///
/// 实现说明：
///   - 可选成本=丢弃手牌中 2 张《和之国》特征卡（HasKeyword("和之国")）；
///   - 收益门槛：我方生命≤1 时，卡组顶 1 张加入生命区最上方(AddLifeFromDeckTop)。
/// </summary>
public class OP04_098_Otoko : IScriptedEffect
{
    public string CardNumber => "OP04-098";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var wano = me.Hand.Where(c => c.Info.HasKeyword("和之国")).ToList();
        if (wano.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿户子【登场时】：丢弃手牌中 2 张《和之国》特征卡，生命≤1 时卡组顶 1 张加入生命？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = wano.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃手牌中 2 张《和之国》特征卡",
            wano.Select(c => c.Id.ToString()).ToList(), 2, 2, extra);
        if (chosen.Count < 2) return; // 成本未支付

        foreach (var id in chosen)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == id);
            if (card != null) AtomicOps.DiscardHand(me, card);
        }

        // 收益：生命≤1 时卡组顶 1 张加入生命区最上方
        if (me.LifeArea.Count <= 1)
            AtomicOps.AddLifeFromDeckTop(me, 1);
    }
}
