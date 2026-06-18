using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST03-007 战桃丸（角色）
/// 【咚!!×1】【启动主要】【每回合1次】②：从我方卡组中将最多1张费用≤4的“和平主义者”登场，并切洗卡组。
/// </summary>
public class ST03_007_Sentomaru : IScriptedEffect
{
    public string CardNumber => "ST03-007";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;
        var key = "ST03-007-Activated" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        if (me.ActiveDonCount < 2) return;
        var cands = me.Deck.Where(c => c.Info.Kind == CardKind.Character && c.Info.NameIs("和平主义者") && c.Info.Cost <= 4).ToList();
        if (cands.Count == 0) return;
        // 支付成本：横置2张活跃咚
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 2) break;
            if (d.State == DonState.Active && d.AttachedToCardId is null) { d.State = DonState.Rest; rested++; }
        }
        if (rested < 2) return;
        me.TurnOnceUsed.Add(key);
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PlayCharFromDeck",
            "从卡组将最多1张费用≤4的“和平主义者”登场", cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (ch.Count > 0)
            AtomicOps.PlayFromDeckFree(ctx.State, ctx.OwnerIndex, cands.First(c => c.Id.ToString() == ch[0]));
        AtomicOps.Shuffle(ctx.State, me.Deck);
    }
}

/// <summary>
/// ST12-010 安普里奥·伊万科夫（角色）
/// 【登场时】公开我方卡组最上方1张，将最多1张费用2的角色登场；之后将剩余的卡牌放回卡组最上方或最下方。
/// 【攻击时】【每回合1次】我方手牌不多于6张的场合，抽取1张卡牌。
/// </summary>
public class ST12_010_Ivankov : IScriptedEffect
{
    public string CardNumber => "ST12-010";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnAttackDeclare)
        {
            var key = $"ST12-010-atk:{ctx.Source.Id}";
            if (me.TurnOnceUsed.Contains(key)) return;
            if (me.Hand.Count <= 6) { AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1); me.TurnOnceUsed.Add(key); }
            return;
        }
        await RevealTopPlayCost2(ctx, me, rest: false);
    }

    internal static async Task RevealTopPlayCost2(EffectContext ctx, PlayerState me, bool rest)
    {
        if (me.Deck.Count == 0) return;
        var top = me.Deck[0];
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new List<string> { top.Info.Number });
        if (top.Info.Kind == CardKind.Character && top.Info.Cost == 2)
        {
            if (await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, $"将公开的「{top.Info.Name}」登场？"))
            {
                AtomicOps.PlayFromDeckFree(ctx.State, ctx.OwnerIndex, top, rest);
                return;
            }
        }
        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "将该牌放回卡组最上方还是最下方？",
            new List<string> { "卡组最上方", "卡组最下方" });
        if (opt == 1) { me.Deck.RemoveAt(0); me.Deck.Add(top); }
    }
}

/// <summary>
/// ST12-013 卓夫（角色）
/// 【登场时】确认我方卡组顶3张，自选顺序放置到卡组最上方或最下方。
/// 【攻击时】公开我方卡组最上方1张，将最多1张费用2的角色以休息状态登场；之后将剩余放回卡组最上方或最下方。
/// </summary>
public class ST12_013_Zoff : IScriptedEffect
{
    public string CardNumber => "ST12-013";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var top = me.Deck.Take(3).ToList();
            if (top.Count == 0) return;
            int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "确认卡组顶3张，放置到卡组最上方还是最下方？",
                new List<string> { "卡组最上方", "卡组最下方" });
            AtomicOps.ReorderTopK(me, top.Select(c => c.Id).ToList(), toBottom: opt == 1);
            return;
        }
        await ST12_010_Ivankov.RevealTopPlayCost2(ctx, me, rest: true);
    }
}
