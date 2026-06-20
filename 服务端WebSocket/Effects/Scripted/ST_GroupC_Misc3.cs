using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST02-010 巴基尔·霍金斯（角色）
/// 【咚!!×1】【我方的回合中】【每回合1次】此角色与对方的角色进行战斗的场合，将此角色转为活跃状态。
/// 实现：以【攻击时】且攻击目标为角色近似“与对方角色战斗”，满足条件则将自身转活跃。
/// </summary>
public class ST02_010_Hawkins : IScriptedEffect
{
    public string CardNumber => "ST02-010";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return Task.CompletedTask;
        var b = ctx.State.CurrentBattle;
        if (b is null || b.AttackerCardId != ctx.Source.Id || b.TargetIsLeader) return Task.CompletedTask;
        var key = $"ST02-010-once:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        AtomicOps.ActivateCard(ctx.Source);
        me.TurnOnceUsed.Add(key);
        return Task.CompletedTask;
    }
}

/// <summary>
/// ST07-010 夏洛特·玲玲（角色）
/// 【登场时】对方选择以下1项：・将对方生命区最上方1张放置废弃区；・将我方卡组最上方1张加入生命区顶。
/// </summary>
public class ST07_010_BigMom : IScriptedEffect
{
    public string CardNumber => "ST07-010";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;
    public Task Resolve(EffectContext ctx) => BigMomChoice.Run(ctx);
}

/// <summary>
/// ST07-015 对魂低语（事件）
/// 【主要】对方选择以下1项：同 ST07-010。【触发】发动此卡牌的【主要】效果。
/// </summary>
public class ST07_015_SoulPocus : IScriptedEffect
{
    public string CardNumber => "ST07-015";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;
    public Task Resolve(EffectContext ctx) => BigMomChoice.Run(ctx);
}

internal static class BigMomChoice
{
    public static async Task Run(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        int oppIdx = 1 - owner;
        var opp = ctx.State.Players[oppIdx];
        var me = ctx.State.Players[owner];
        // 由对方做出选择
        int opt = await ctx.Prompts.ChooseOption(oppIdx, "对方玲玲：选择以下1项",
            new List<string> { "将你（对方）生命区最上方1张放置废弃区", "让对方卡组顶1张加入其生命区" });
        if (opt == 0)
        {
            if (opp.LifeArea.Count > 0) { var top = opp.LifeArea[0]; opp.LifeArea.RemoveAt(0); opp.Trash.Add(top); }
        }
        else
        {
            AtomicOps.AddLifeFromDeckTop(me, 1);
        }
    }
}

/// <summary>
/// ST12-001 罗罗诺亚·佐罗&山智（领航）
/// 【咚!!×1】【攻击时】【每回合1次】可以将我方1张费用≥2的角色放回手牌：将我方最多1张力量≤7000的角色转为活跃状态。
/// </summary>
public class ST12_001_ZoroSanji : IScriptedEffect
{
    public string CardNumber => "ST12-001";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;
        var key = "ST12-001-Activated" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        // 成本：退回我方1张费用≥2角色（可选；不退则不发动）
        var costCands = me.Characters.Where(c => c.Info.Cost >= 2).ToList();
        if (costCands.Count == 0) return;
        var costCh = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方1张费用≥2的角色放回手牌（成本，可放弃）", costCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (costCh.Count == 0) return;
        var costCard = costCands.First(c => c.Id.ToString() == costCh[0]);
        AtomicOps.BounceToHand(ctx.State, ctx.OwnerIndex, costCard);
        me.TurnOnceUsed.Add(key);
        // 效果：活跃我方最多1张力量≤7000角色
        var cands = me.Characters.Where(c => ctx.State.CurrentPowerOf(ctx.OwnerIndex, c) <= 7000).ToList();
        if (cands.Count == 0) return;
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方最多1张力量≤7000角色转为活跃状态", cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (ch.Count > 0) AtomicOps.ActivateCard(cands.First(c => c.Id.ToString() == ch[0]));
    }
}
