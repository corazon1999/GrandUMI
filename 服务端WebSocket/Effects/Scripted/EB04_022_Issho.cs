using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-022 一生（角色 / 水 / 德莱斯罗兹・海军 / cost5 power7000）
/// 【登场时】可以丢弃我方的2张手牌：对方持有6张或更多手牌的场合，对方将其2张手牌自选顺序放回卡组最下方。
/// 【咚!!×1】【攻击时】可以丢弃我方的1张手牌：本回合中，对方最多1张角色力量-2000。
///
/// 实现说明：
///   - 登场段为可选成本（"可以…：…"）：先 ConfirmOptional；我方手牌<2 张无法支付。
///     支付后仅当对方手牌≥6 张才令对方选 2 张放回卡组最下方（prompt 发给对方）。
///   - 攻击段：贴咚条件【咚!!×1】= 自身被赋予中咚!!≥1；可选成本弃我方 1 张手牌；
///     之后对方最多 1 张角色本回合力量-2000。
/// </summary>
public class EB04_022_Issho : IScriptedEffect
{
    public string CardNumber => "EB04-022";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (me.Hand.Count < 2) return; // 无法支付成本（弃 2 张）

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "一生【登场时】：丢弃我方 2 张手牌（对方手牌≥6 张时令其将 2 张手牌放回卡组最下方）？");
            if (!use) return;

            // 成本：丢弃我方 2 张手牌
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
                "丢弃我方 2 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 2, 2, extra);
            if (disc.Count < 2)
            {
                // 自动从头丢 2 张
                for (int i = 0; i < 2 && me.Hand.Count > 0; i++) AtomicOps.DiscardHand(me, me.Hand[0]);
            }
            else
            {
                foreach (var id in disc)
                {
                    var c = me.Hand.FirstOrDefault(x => x.Id.ToString() == id);
                    if (c is not null) AtomicOps.DiscardHand(me, c);
                }
            }

            // 收益：对方手牌≥6 张 → 对方选 2 张放回卡组最下方
            if (opp.Hand.Count < 6) return;
            var oppExtra = new Dictionary<string, object?>
            {
                ["choiceCards"] = opp.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var picked = await ctx.Prompts.ChooseCards(1 - ctx.OwnerIndex, "OwnHand",
                "将你的 2 张手牌放回卡组最下方", opp.Hand.Select(c => c.Id.ToString()).ToList(), 2, 2, oppExtra);
            var toReturn = new List<CardInstance>();
            if (picked.Count >= 2)
                foreach (var id in picked)
                {
                    var c = opp.Hand.FirstOrDefault(x => x.Id.ToString() == id);
                    if (c is not null) toReturn.Add(c);
                }
            else
                toReturn.AddRange(opp.Hand.Take(2)); // 超时自动取前 2 张

            foreach (var c in toReturn) AtomicOps.ReturnHandToDeckBottom(opp, c);
            return;
        }

        // 【咚!!×1】【攻击时】
        if (ctx.Trigger == EffectTrigger.OnAttackDeclare)
        {
            // 贴咚条件：自身被赋予中咚!!≥1
            if (me.AttachedDonCount(ctx.Source.Id) < 1) return;
            if (me.Hand.Count < 1) return; // 无法支付成本（弃 1 张）

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "一生【攻击时】：丢弃我方 1 张手牌，使对方最多 1 张角色本回合力量-2000？");
            if (!use) return;

            // 成本：弃我方 1 张手牌
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandDiscard",
                "丢弃我方 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
            if (disc.Count >= 1)
            {
                var c = me.Hand.FirstOrDefault(x => x.Id.ToString() == disc[0]);
                if (c is not null) AtomicOps.DiscardHand(me, c);
            }
            else if (me.Hand.Count > 0) AtomicOps.DiscardHand(me, me.Hand[0]);

            // 效果：对方最多 1 张角色 -2000
            if (opp.Characters.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张角色，本回合力量-2000",
                opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = opp.Characters.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddPowerThisTurn(tgt, -2000);
            }
        }
    }
}
