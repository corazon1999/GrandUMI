using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-041 奈美（领航）
/// 【我方的回合中】【每回合1次】当生命卡牌离开场上时可以发动。我方手牌不多于 7 张的场合，抽取 1 张卡牌。
/// 【咚!!×1】【对方的攻击时】【每回合1次】可以丢弃我方的 1 张手牌：本回合中，此领袖的力量 +2000。
///
/// 实现说明：
///   - 第一段【我方回合中】【每回合1次】生命卡牌离场时、手牌≤7 抽1：监听 OnLifeLeaveField
///     （事件在 Game/LifeRevealManager 已派发，参考 OP08_105）。不限离场方——我方回合中生命离场通常
///     是攻击致对方生命离场，也覆盖自身生命离场，符合原文未指明哪方。
///   - 第二段【咚!!×1】【对方的攻击时】：要求此领袖已被赋予 ≥1 张咚!!（【咚!!×1】），每回合 1 次，
///     可选丢弃 1 张手牌后，本回合此领袖力量 +2000。用 OnOppAttackDeclare 触发。
/// </summary>
public class OP11_041_Nami : IScriptedEffect
{
    public string CardNumber => "OP11-041";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnOppAttackDeclare || t == EffectTrigger.OnLifeLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 仅领袖自身效果
        if (self.Id != me.Leader.Id) return;

        // ── 第一段【我方回合中】【每回合1次】生命卡牌离场时，手牌≤7 则抽 1 ──
        if (ctx.Trigger == EffectTrigger.OnLifeLeaveField)
        {
            if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return; // 仅我方回合
            var lifeKey = self.Info.Number + "-lifedraw";
            if (me.TurnOnceUsed.Contains(lifeKey)) return;             // 每回合 1 次
            if (me.Hand.Count > 7) return;                             // 手牌不多于 7 张
            me.TurnOnceUsed.Add(lifeKey);
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        // ── 以下为第二段【咚!!×1】【对方的攻击时】──
        // 【咚!!×1】：此领袖需被赋予至少 1 张咚!!
        if (me.AttachedDonCount(self.Id) < 1) return;

        // 【每回合1次】
        var key = self.Info.Number + "-counter" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：可以丢弃我方 1 张手牌
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "奈美【对方的攻击时】：丢弃 1 张手牌，使此领袖本回合力量 +2000？");
        if (!use) return;

        var costExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var paid = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张手牌作为成本",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, costExtra);
        if (paid.Count < 1) return;

        var costCard = me.Hand.First(c => c.Id.ToString() == paid[0]);
        AtomicOps.DiscardHand(me, costCard);

        me.TurnOnceUsed.Add(key);

        AtomicOps.AddPowerThisTurn(self, 2000);
    }
}
