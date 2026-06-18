using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-019 武装色的霸气（事件）
/// 【主要】可以赋予我方的 1 张"希尔巴兹·雷利"1 张活跃状态的咚!!：
///   本回合中，我方最多 1 张领袖或角色力量+1000。
/// 【反击】本次战斗中，我方最多 1 张角色或"希尔巴兹·雷利"力量+2000。
///
/// 说明：
/// - 【主要】费用为可选（无可赋予对象 / 无活跃咚时无法发动，跳过）。
/// - 【反击】"角色或希尔巴兹·雷利"——可选对象为我方所有角色，外加领袖（仅当其名为"希尔巴兹·雷利"）。
/// </summary>
public class OP12_019_Busoshoku : IScriptedEffect
{
    public string CardNumber => "OP12-019";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventMain) await ResolveMain(ctx);
        else if (ctx.Trigger == EffectTrigger.EventCounter) await ResolveCounter(ctx);
    }

    private async Task ResolveMain(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 费用：赋予我方 1 张"希尔巴兹·雷利"1 张活跃咚
        var rayleighTargets = new List<CardInstance>();
        if (me.Leader.MatchesName("希尔巴兹·雷利")) rayleighTargets.Add(me.Leader);
        rayleighTargets.AddRange(me.Characters.Where(c => c.MatchesName("希尔巴兹·雷利")));
        if (rayleighTargets.Count == 0 || me.ActiveDonCount < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "武装色的霸气：赋予 1 张\"希尔巴兹·雷利\"1 张活跃咚，使我方 1 张领袖或角色本回合力量+1000？");
        if (!use) return;

        var pickTgt = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnRayleigh",
            "选择 1 张\"希尔巴兹·雷利\"赋予 1 张活跃咚",
            rayleighTargets.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pickTgt.Count == 0) return;
        var donTarget = rayleighTargets.First(c => c.Id.ToString() == pickTgt[0]);
        AtomicOps.AttachDonFromCost(me, donTarget.Id, 1, DonState.Active);

        // 效果：本回合中，我方最多 1 张领袖或角色力量+1000
        var buffTargets = new List<CardInstance> { me.Leader };
        buffTargets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张领袖或角色，本回合力量+1000",
            buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var t = buffTargets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(t, 1000);
        }
    }

    private async Task ResolveCounter(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var buffTargets = new List<CardInstance>(me.Characters);
        if (me.Leader.MatchesName("希尔巴兹·雷利")) buffTargets.Add(me.Leader);
        if (buffTargets.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharOrRayleigh",
            "选择最多 1 张角色或\"希尔巴兹·雷利\"，本次战斗力量+2000",
            buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var t = buffTargets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(t, 2000);
        }
    }
}
