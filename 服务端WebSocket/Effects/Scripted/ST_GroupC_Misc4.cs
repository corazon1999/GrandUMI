using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST13-001 萨波（领航）
/// 【咚!!×1】【启动主要】【每回合1次】可以将我方1张费用≥3且力量≥7000的角色正面朝上加入生命区最上方：
///   直到下个我方回合开始，我方最多1张角色力量+2000。
/// （生命区无“正面朝上”状态，按普通置入生命近似）
/// </summary>
public class ST13_001_Sabo : IScriptedEffect
{
    public string CardNumber => "ST13-001";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;
        var key = "ST13-001-Activated" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        var moveCands = me.Characters.Where(c => c.Info.Cost >= 3 && ctx.State.CurrentPowerOf(ctx.OwnerIndex, c) >= 7000).ToList();
        if (moveCands.Count == 0) return;
        var mch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方1张费用≥3且力量≥7000角色加入生命区顶（成本，可放弃）", moveCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (mch.Count == 0) return;
        var moved = moveCands.First(c => c.Id.ToString() == mch[0]);
        AtomicOps.MoveCharToLife(ctx.State, ctx.OwnerIndex, moved, toTop: true);
        me.TurnOnceUsed.Add(key);
        var buffCands = me.Characters.ToList();
        if (buffCands.Count == 0) return;
        var bch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "我方最多1张角色力量+2000（直到下个我方回合开始）", buffCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (bch.Count > 0) AtomicOps.AddPowerPersistent(buffCands.First(c => c.Id.ToString() == bch[0]), 2000);
    }
}

/// <summary>
/// ST13-009 杰克斯（7费角色）
/// 【登场时】可以将我方1张正面朝上的生命卡牌翻面：对方持有7张或更多手牌的场合，将对方生命区最上方的最多1张卡牌放置到废弃区。
/// （无“正面朝上生命”状态，翻面成本省略；保留“对方手牌≥7 则废弃对方生命顶”的主体）
/// </summary>
public class ST13_009_Shanks : IScriptedEffect
{
    public string CardNumber => "ST13-009";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        if (opp.Hand.Count < 7) return;
        if (opp.LifeArea.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "杰克斯：将对方生命区最上方1张置入废弃区？")) return;
        var top = opp.LifeArea[0];
        opp.LifeArea.RemoveAt(0);
        opp.Trash.Add(top);
    }
}

/// <summary>
/// ST26-001 面条假面（角色）
/// 【登场时】将我方所有的“山五郎”和“山智”放回其持有者的手牌。
/// （“场上有力量≥7000的山五郎/山智时手牌中此卡费用-5”由 HandStaticCost 实现）
/// </summary>
public class ST26_001_NoodleMask : IScriptedEffect
{
    public string CardNumber => "ST26-001";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        foreach (var c in me.Characters.Where(c => c.Info.NameIs("山五郎") || c.Info.NameIs("山智")).ToList())
            AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, c);
        return Task.CompletedTask;
    }
}
