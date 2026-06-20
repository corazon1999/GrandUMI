using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST09-010 波特夹斯·D·艾斯（角色）
/// 【每回合1次】此角色将要被KO的场合，可以改为将我方生命区最上方的1张卡牌放置到废弃区，使此角色不会被KO。
/// （守护替身，仅覆盖经 KO 流程的被KO；生命取顶部近似“最上方或最下方”）
/// </summary>
public class ST09_010_Ace : IScriptedEffect
{
    public string CardNumber => "ST09-010";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.PreKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var key = $"ST09-010-guard:{self.Id}";
        if (me.TurnOnceUsed.Contains(key)) return;
        if (me.LifeArea.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "艾斯：将生命区最上方1张置入废弃区，使此角色不被KO？")) return;
        var top = me.LifeArea[0]; me.LifeArea.RemoveAt(0); me.Trash.Add(top);
        ctx.State.MarkPreventKO(self.Id);
        me.TurnOnceUsed.Add(key);
    }
}

/// <summary>
/// ST20-002 夏洛特·克力架（角色）
/// 【每回合1次】此角色因效果将要被KO的场合，可以改为将我方生命区最上方的1张卡牌放置到废弃区，使此角色不会被KO。
/// （未对“仅因效果”做上下文限制，对战斗KO也生效，略宽松）
/// </summary>
public class ST20_002_Cracker : IScriptedEffect
{
    public string CardNumber => "ST20-002";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.PreKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        var key = $"ST20-002-guard:{self.Id}";
        if (me.TurnOnceUsed.Contains(key)) return;
        if (me.LifeArea.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "克力架：将生命区最上方1张置入废弃区，使此角色不被KO？")) return;
        var top = me.LifeArea[0]; me.LifeArea.RemoveAt(0); me.Trash.Add(top);
        ctx.State.MarkPreventKO(self.Id);
        me.TurnOnceUsed.Add(key);
    }
}

/// <summary>
/// ST29-008 奈美（角色）
/// 我方拥有《艾格赫德》特征的角色因对方的效果将要被KO的场合，可以改为将我方生命区最上方的1张卡牌翻至正面朝上，使该角色不会被KO。
/// 实现说明：引擎无“正面朝上生命”状态，成本以“生命顶置入废弃”近似（更严格但有界）；守护自身(PreKO)与我方艾格赫德角色(OnAllyWillBeKOd)。
/// </summary>
public class ST29_008_Nami : IScriptedEffect
{
    public string CardNumber => "ST29-008";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.PreKO || t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        CardInstance? victim = ctx.Trigger == EffectTrigger.PreKO
            ? ctx.Source
            : me.Characters.FirstOrDefault(c => c.Id.ToString() == (ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null));
        if (victim is null) return;
        if (!victim.Info.HasKeyword("艾格赫德")) return;
        if (me.LifeArea.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "奈美：消耗1张生命，使该《艾格赫德》角色不被KO？")) return;
        var top = me.LifeArea[0]; me.LifeArea.RemoveAt(0); me.Trash.Add(top);
        ctx.State.MarkPreventKO(victim.Id);
    }
}
