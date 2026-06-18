using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

internal static class LifeScry
{
    /// <summary>确认我方或对方生命区最上方1张，放置到该生命区最上方或最下方。（"确认"按位置选择近似，未向控制者明示牌面）</summary>
    public static async Task ConfirmTopAndReposition(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        int side = await ctx.Prompts.ChooseOption(owner, "确认哪一方生命区最上方的卡牌？",
            new List<string> { "我方生命区", "对方生命区" });
        var p = ctx.State.Players[side == 0 ? owner : 1 - owner];
        if (p.LifeArea.Count == 0) return;
        int pos = await ctx.Prompts.ChooseOption(owner, "将该卡放置到生命区最上方还是最下方？",
            new List<string> { "最上方", "最下方" });
        if (pos == 1) { var top = p.LifeArea[0]; p.LifeArea.RemoveAt(0); p.LifeArea.Add(top); }
    }
}

/// <summary>
/// ST07-003 夏洛特·卡塔库栗（角色）
/// 【登场时】确认我方或对方生命区最上方最多1张放生命顶/底。之后，我方生命卡牌比对方少的场合，本回合此角色获得【速攻】。
/// </summary>
public class ST07_003_Katakuri : IScriptedEffect
{
    public string CardNumber => "ST07-003";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        await LifeScry.ConfirmTopAndReposition(ctx);
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        if (me.LifeCount < opp.LifeCount) AtomicOps.GiveKeyword(ctx.Source, "速攻", KeywordDuration.ThisTurn);
    }
}

/// <summary>ST07-008 夏洛特·布玲（角色）【登场时】确认我方或对方生命顶1张放生命顶/底。</summary>
public class ST07_008_Brulee : IScriptedEffect
{
    public string CardNumber => "ST07-008";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;
    public Task Resolve(EffectContext ctx) => LifeScry.ConfirmTopAndReposition(ctx);
}

/// <summary>
/// ST20-003 夏洛特·布蕾（角色）
/// 【触发】确认我方或对方生命顶1张放生命顶/底。之后，将此卡牌加入手牌。
/// </summary>
public class ST20_003_Brulee : IScriptedEffect
{
    public string CardNumber => "ST20-003";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        await LifeScry.ConfirmTopAndReposition(ctx);
        AtomicOps.TrashToHand(ctx.State.Players[ctx.OwnerIndex], ctx.Source);
    }
}

/// <summary>
/// ST13-012 卷乃（角色）
/// 【登场时】可以将我方生命区最上方1张加入手牌：确认我方所有生命卡牌，自选顺序放置到生命区。
/// （"自选顺序重排"无逐张排序 UI，简化为保持原序；核心是可将1张生命入手）
/// </summary>
public class ST13_012_Makino : IScriptedEffect
{
    public string CardNumber => "ST13-012";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return; // ST15-001
        if (me.LifeArea.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将我方生命区最上方1张加入手牌？")) return;
        var top = me.LifeArea[0]; me.LifeArea.RemoveAt(0); me.Hand.Add(top);
    }
}

/// <summary>
/// ST13-016 大和（5费角色）
/// 【速攻】【登场时】确认我方所有生命卡牌，将其中1张放置到卡组最上方，其余自选顺序放回生命区。
/// </summary>
public class ST13_016_Yamato : IScriptedEffect
{
    public string CardNumber => "ST13-016";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.LifeArea.Count == 0) return;
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLifeAll",
            "确认全部生命，将其中1张放置到卡组最上方", me.LifeArea.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (ch.Count == 0) return;
        var picked = me.LifeArea.First(c => c.Id.ToString() == ch[0]);
        me.LifeArea.Remove(picked);
        me.Deck.Insert(0, picked);
    }
}
