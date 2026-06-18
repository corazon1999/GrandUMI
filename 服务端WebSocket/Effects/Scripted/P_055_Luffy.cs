using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-055 蒙奇·D·路飞（角色 / 水 4 费 5000 / 草帽一伙）
/// 【登场时】可以丢弃我方的2张手牌：对方将其1张角色放回其持有者的卡组最下方。
///
/// 实现说明：
///   - 成本：丢弃我方 2 张手牌（"可以"型，可选）。需手牌≥2 且对方有角色才有意义。
///   - 收益："对方将其1张角色放回卡组最下方"——由对方选择放回哪张（prompt 发给对方）。
///   - 用 ReturnFieldToDeckBottom 将对方所选角色放回其卡组底。
/// </summary>
public class P_055_Luffy : IScriptedEffect
{
    public string CardNumber => "P-055";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本要求：我方手牌（除本次登场需排除自身外，本卡此时已在场上不在手牌）≥2，对方有角色
        if (me.Hand.Count < 2) return;
        if (opp.Characters.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞【登场时】：丢弃我方 2 张手牌，使对方将其 1 张角色放回卡组最下方？");
        if (!use) return;

        // 成本：丢弃我方 2 张手牌（由我方选择）
        var handExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var discard = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方 2 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 2, 2, handExtra);
        if (discard.Count < 2) return; // 成本未支付

        foreach (var id in discard)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.DiscardHand(me, card);
        }

        // 收益：对方将其 1 张角色放回卡组最下方（由对方选择）
        if (opp.Characters.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(1 - ctx.OwnerIndex, "OwnCharacter",
            "将你的 1 张角色放回卡组最下方",
            opp.Characters.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count > 0)
        {
            var tgt = opp.Characters.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.ReturnFieldToDeckBottom(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
