using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST08-005 杰克斯（角色）
/// 【登场时】可以丢弃我方的1张手牌：将所有费用不高于1的角色KO。
/// </summary>
public class ST08_005_Shanks : IScriptedEffect
{
    public string CardNumber => "ST08-005";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Hand.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "杰克斯：丢弃1张手牌，将所有费用≤1的角色KO？")) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand", "丢弃1张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (card is null) return;
        AtomicOps.DiscardHand(me, card);
        // 双方所有费用≤1角色KO（快照后逐一KO）
        for (int p = 0; p < 2; p++)
        {
            foreach (var c in ctx.State.Players[p].Characters.Where(c => c.Info.Cost <= 1).ToList())
                AtomicOps.KO(ctx.State, p, c);
        }
    }
}

/// <summary>
/// ST08-009 卷乃（角色）
/// 【登场时】场上存在费用为0的角色的场合，抽取1张卡牌。
/// </summary>
public class ST08_009_Makino : IScriptedEffect
{
    public string CardNumber => "ST08-009";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        bool boardHasCost0 = ctx.State.Players[0].Characters.Concat(ctx.State.Players[1].Characters).Any(c => c.Info.Cost == 0);
        if (boardHasCost0) AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        return Task.CompletedTask;
    }
}

/// <summary>
/// ST10-004 山智（角色）
/// 【登场时】对方场上存在力量5000或更高的角色的场合，本回合中，此角色获得【速攻】效果。
/// </summary>
public class ST10_004_Sanji : IScriptedEffect
{
    public string CardNumber => "ST10-004";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        bool oppBig = ctx.State.Players[oppIdx].Characters.Any(c => ctx.State.CurrentPowerOf(oppIdx, c) >= 5000);
        if (oppBig) AtomicOps.GiveKeyword(ctx.Source, "速攻", KeywordDuration.ThisTurn);
        return Task.CompletedTask;
    }
}
