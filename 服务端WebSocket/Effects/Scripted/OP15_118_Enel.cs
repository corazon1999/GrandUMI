using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-118 艾尼路（角色 / 光 / 空岛）
/// 我方场上咚!!的张数不多于 6 张的场合，此角色不会因对方的效果而离场，且力量 +2000。（持续）
/// 【登场时】咚!!-1：确认我方卡组最上方的 5 张卡牌，将其中最多 1 张卡牌加入手牌。
///   之后，将剩余的卡牌自选顺序放回卡组最下方，并丢弃我方的 1 张手牌。
///
/// 实现说明：
///   - 持续段：ContinuousEffect{ PowerDelta=2000, LeaveGuard="effect" }，Predicate 限自身且我方费用区
///     咚总数 TotalDonInCostArea ≤ 6（防离场参照 EB04_057_Vegapunk；引擎各离场口查 IsLeaveGuarded）。
///   - 登场段：成本「咚!!-1」用 ReturnDonToDeck(me,1)，无咚可返还则登场段不发动（持续段仍注册）。
///     支付后探顶 5 张选 ≤1（任意）入手，余按原序放回卡组底，最后强制丢弃我方 1 张手牌。
/// </summary>
public class OP15_118_Enel : IScriptedEffect
{
    public string CardNumber => "OP15-118";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;

        // 持续：场上咚 ≤6 时，自身不会因对方效果离场 + 力量 +2000
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            LeaveGuard = "effect",
            Predicate = (s, sideIdx, card) =>
                sideIdx == owner && card.Id == selfId &&
                s.Players[owner].TotalDonInCostArea <= 6,
        });

        // 【登场时】成本「咚!!-1」：返还 1 张咚到咚卡组；无咚可返还或取消则登场段不发动
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        // 探顶 5 张，将其中最多 1 张（任意）加入手牌
        int peek = Math.Min(5, me.Deck.Count);
        if (peek > 0)
        {
            var top = me.Deck.Take(peek).ToList();
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = top.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LookTopReveal",
                "确认卡组顶 5 张，将其中最多 1 张加入手牌",
                top.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            foreach (var cid in chosen)
            {
                var picked = top.FirstOrDefault(c => c.Id.ToString() == cid);
                if (picked is not null) { me.Deck.Remove(picked); me.Hand.Add(picked); }
            }
            // 剩余仍在顶部的卡按原相对顺序放回卡组最下方
            var rest = top.Where(c => me.Deck.Contains(c)).ToList();
            foreach (var c in rest) me.Deck.Remove(c);
            me.Deck.AddRange(rest);
        }

        // 之后：丢弃我方 1 张手牌（强制）
        if (me.Hand.Count > 0)
        {
            var dch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "丢弃 1 张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (dch.Count >= 1)
            {
                var dcard = me.Hand.First(c => c.Id.ToString() == dch[0]);
                AtomicOps.DiscardHand(me, dcard);
            }
        }
    }
}
