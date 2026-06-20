using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST25-003 克洛克达尔&米霍克（角色）
/// 【每回合1次】我方拥有《十字公会》特征的角色因对方的效果将要离开场上的场合，可以改为丢弃我方1张手牌，使该角色不离场。
/// 实现说明：仅覆盖经 KO 流程的被KO（OnAllyWillBeKOd）；非KO离场（退手/退底）引擎无置换钩子，未覆盖。
/// （登场时抽2丢1+登场十字公会 由 DSL ST25.json 实现）
/// </summary>
public class ST25_003_CrocodileMihawk : IScriptedEffect
{
    public string CardNumber => "ST25-003";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victim is null || !victim.Info.HasKeyword("十字公会")) return;
        var key = $"ST25-003-guard:{self.Id}";
        if (me.TurnOnceUsed.Contains(key)) return;
        if (me.Hand.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "克洛克达尔&米霍克：丢弃1张手牌，使该《十字公会》角色不被KO？")) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand", "丢弃1张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;
        var card = me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (card is null) return;
        AtomicOps.DiscardHand(me, card);
        ctx.State.MarkPreventKO(victim.Id);
        me.TurnOnceUsed.Add(key);
    }
}

/// <summary>
/// ST30-009 小奥兹Jr.（角色）
/// 我方原本力量为6000的角色因对方的效果将要离开场上的场合，可以改为将此角色放置到废弃区并抽取1张卡牌，使该角色不离场。
/// 实现说明：仅覆盖经 KO 流程的被KO；成本=自身入废弃 + 抽1。
/// </summary>
public class ST30_009_LittleOarsJr : IScriptedEffect
{
    public string CardNumber => "ST30-009";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victim is null || victim.Info.Power != 6000) return;
        if (victim.Id == self.Id) return; // 替身须为他卡
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "小奥兹Jr.：将自身置入废弃区并抽1张，使该力量6000角色不被KO？")) return;
        BattleEngine.KOCard(ctx.State, ctx.OwnerIndex, self);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        ctx.State.MarkPreventKO(victim.Id);
    }
}

/// <summary>
/// ST30-011 巴奇（角色）
/// 我方原本力量为6000的角色因对方的效果将要离开场上的场合，可以改为将此角色转为休息状态，使该角色不离场。
/// 实现说明：仅覆盖经 KO 流程的被KO；成本=自身转休息。
/// </summary>
public class ST30_011_Buggy : IScriptedEffect
{
    public string CardNumber => "ST30-011";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victim is null || victim.Info.Power != 6000) return;
        if (victim.Id == self.Id) return;
        if (self.IsTapped) return; // 已休息无法支付
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "巴奇：将自身转为休息状态，使该力量6000角色不被KO？")) return;
        AtomicOps.RestCard(self);
        ctx.State.MarkPreventKO(victim.Id);
    }
}
