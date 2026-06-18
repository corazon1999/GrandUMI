using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-042 居鲁士（水 3 费 5000，德莱斯罗兹）
/// 【登场时】可以丢弃我方的 1 张手牌：我方领袖为"莉贝卡"的场合，本回合中，此角色获得【速攻】效果。
/// 【KO时】将此角色卡牌从废弃区加入手牌。
///
/// 实现说明：
///   - 【登场时】为可选弃牌成本（ConfirmOptional 后选 1 张手牌丢弃）；
///     条件为领袖名含"莉贝卡"时本回合获得【速攻】（GiveKeyword, ThisTurn）。
///     成本独立于条件：先支付（丢手牌），领袖非莉贝卡则仅不获得速攻。手牌为空无法支付。
///   - 【KO时】此卡此时已在废弃区，从废弃区取回手牌（TrashToHand）。
/// </summary>
public class OP15_042_Kyros : IScriptedEffect
{
    public string CardNumber => "OP15-042";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            // 此卡此时位于废弃区，取回手牌
            if (me.Trash.Contains(self))
            {
                AtomicOps.TrashToHand(me, self);
            }
            return;
        }

        // ── 【登场时】可选弃牌成本 ──
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "居鲁士【登场时】：丢弃我方 1 张手牌，若领袖为\"莉贝卡\"则本回合此角色获得【速攻】？");
        if (!use) return;

        var discardExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var dchosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Kyros042Discard",
            "丢弃我方 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, discardExtra);
        var discard = dchosen.Count > 0
            ? me.Hand.FirstOrDefault(c => c.Id.ToString() == dchosen[0])
            : me.Hand[0];
        if (discard is null) return;
        AtomicOps.DiscardHand(me, discard);

        // 条件：领袖为"莉贝卡"
        if (!me.Leader.Info.NameContains("莉贝卡")) return;

        AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.ThisTurn);
    }
}
