using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST03-010 巴索罗缪·大熊（角色）
/// 【登场时】确认我方卡组最上方的3张卡牌，将其自选顺序排列并放置到卡组的最上方或最下方。
/// （“自选顺序”简化为原相对顺序，仅让玩家选放顶/底）
/// </summary>
public class ST03_010_Kuma : IScriptedEffect
{
    public string CardNumber => "ST03-010";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var top = me.Deck.Take(3).ToList();
        if (top.Count == 0) return;
        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "确认卡组顶3张，放置到卡组最上方还是最下方？",
            new List<string> { "卡组最上方", "卡组最下方" });
        AtomicOps.ReorderTopK(me, top.Select(c => c.Id).ToList(), toBottom: opt == 1);
    }
}

/// <summary>
/// ST17-003 巴奇（角色）
/// 【登场时】确认我方卡组最上方的3张卡牌，将其自选顺序排列并放回卡组最上方。
/// </summary>
public class ST17_003_Buggy : IScriptedEffect
{
    public string CardNumber => "ST17-003";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var top = me.Deck.Take(3).ToList();
        if (top.Count == 0) return Task.CompletedTask;
        // 仅放回最上方（自选顺序简化为原相对顺序）
        AtomicOps.ReorderTopK(me, top.Select(c => c.Id).ToList(), toBottom: false);
        return Task.CompletedTask;
    }
}

/// <summary>
/// ST12-003 杰拉基尔·米霍克（角色）
/// 【登场时】我方场上的角色不多于2张的场合，将我方手牌中最多1张“米霍克”以外的费用≤4且拥有《阴森王国》特征或属性(斩)的角色卡牌以休息状态登场。
/// </summary>
public class ST12_003_Mihawk : IScriptedEffect
{
    public string CardNumber => "ST12-003";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Characters.Count > 2) return;
        var cands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 4 &&
            !c.Info.NameIs("杰拉基尔·米霍克") &&
            (c.Info.HasKeyword("阴森王国") || c.Info.Property == "斩")).ToList();
        if (cands.Count == 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "PlayCharFromHand",
            "将手牌中最多1张费用≤4《阴森王国》或属性(斩)角色以休息状态登场",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (ch.Count == 0) return;
        var picked = cands.First(c => c.Id.ToString() == ch[0]);
        AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        AtomicOps.RestCard(picked);
    }
}

/// <summary>
/// ST12-007 莉香（角色）
/// 【登场时】②：对方生命卡牌为3张或更多的场合，将我方最多1张费用≤4且属性(斩)的角色转为活跃状态。
/// </summary>
public class ST12_007_Rika : IScriptedEffect
{
    public string CardNumber => "ST12-007";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        if (opp.LifeCount < 3) return;
        if (me.ActiveDonCount < 2) return;
        var cands = me.Characters.Where(c => c.Info.Cost <= 4 && c.Info.Property == "斩").ToList();
        if (cands.Count == 0) return;
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方最多1张费用≤4属性(斩)角色转为活跃状态", cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (ch.Count == 0) return;
        // 支付成本：横置2张活跃咚
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 2) break;
            if (d.State == DonState.Active && d.AttachedToCardId is null) { d.State = DonState.Rest; rested++; }
        }
        if (rested < 2) return;
        AtomicOps.ActivateCard(cands.First(c => c.Id.ToString() == ch[0]));
    }
}

/// <summary>
/// ST28-003 锦卫门（角色）
/// 【触发】我方领袖拥有《和之国》特征，且对方生命卡牌不多于3张的场合，此卡牌登场。
/// （生命触发：解析时本卡已在废弃区，满足条件则从废弃区登场）
/// </summary>
public class ST28_003_Kinemon : IScriptedEffect
{
    public string CardNumber => "ST28-003";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        if (me.Leader.Info.HasKeyword("和之国") && opp.LifeCount <= 3)
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
        return Task.CompletedTask;
    }
}

/// <summary>
/// ST29-005 甚平（角色）
/// 【触发】我方领袖为“蒙奇·D·路飞”的场合，此卡牌登场。
/// </summary>
public class ST29_005_Jinbe : IScriptedEffect
{
    public string CardNumber => "ST29-005";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Leader.Info.NameIs("蒙奇·D·路飞"))
            AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
        return Task.CompletedTask;
    }
}
