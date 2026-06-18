using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-059 闪耀·炸裂（事件，水 / 白胡子海盗团）
/// 【主要】可以将我方 1 张角色放回其持有者的手牌：将最多 1 张费用不高于 6 的角色
///         放回其持有者的手牌。
/// 【触发】抽取 1 张卡牌。
///
/// 实现说明：
///   - 【主要】为可选效果：发动需先支付成本——将我方 1 张角色放回手牌（无我方角色则无法发动）。
///     成本支付后，再选「最多 1 张费用不高于 6 的角色」（敌我双方任一角色，min=0 可跳过）
///     放回其持有者的手牌。费用取角色原本费用 c.Info.Cost。
///   - 【触发】= 抽 1 张。
/// </summary>
public class OP13_059_ShiningBurst : IScriptedEffect
{
    public string CardNumber => "OP13-059";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];

        // ── 【触发】 ──
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            AtomicOps.Draw(s, ctx.OwnerIndex, 1);
            return;
        }

        // ── 【主要】 ──
        // 成本：将我方 1 张角色放回手牌（无我方角色则无法发动）
        if (me.Characters.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP13-059【主要】：将我方 1 张角色放回手牌，以将最多 1 张费用≤6 的角色放回其持有者手牌？");
        if (!use) return;

        var costChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "成本：选择我方 1 张角色放回手牌",
            me.Characters.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (costChosen.Count == 0) return;
        var costTarget = me.Characters.First(c => c.Id.ToString() == costChosen[0]);
        AtomicOps.BounceToHand(s, ctx.OwnerIndex, costTarget);

        // 效果：将最多 1 张费用≤6 的角色（敌我任一）放回其持有者手牌
        var candidates = new List<(CardInstance card, int owner)>();
        candidates.AddRange(me.Characters.Where(c => c.Info.Cost <= 6).Select(c => (c, ctx.OwnerIndex)));
        candidates.AddRange(opp.Characters.Where(c => c.Info.Cost <= 6).Select(c => (c, 1 - ctx.OwnerIndex)));
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "选择最多 1 张费用≤6 的角色，放回其持有者手牌",
            candidates.Select(t => t.card.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var picked = candidates.First(t => t.card.Id.ToString() == chosen[0]);
        AtomicOps.BounceToHand(s, picked.owner, picked.card);
    }
}
