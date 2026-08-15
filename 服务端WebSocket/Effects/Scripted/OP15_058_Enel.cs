using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-058 艾尼路（领航）
/// 持续规则：我方咚!!卡组变为 6 张（见 GameEngine.InitDonDeck）。
/// 【启动主要】【每回合1次】我方第2回合及之后：从咚!!卡组追加最多1张活跃咚 +
///   最多4张休息咚；之后，赋予我方1张角色最多4张休息状态的咚!!。
/// </summary>
public class OP15_058_Enel : IScriptedEffect
{
    public string CardNumber => "OP15-058";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;
    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var key = "OP15-058-MainOncePerTurn" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        if (ctx.State.TurnCount < 2) return;

        // ① 两段均为「最多」：分别选择追加数量，不再自动把可追加数量全部用完。
        int activeMax = Math.Min(1, Math.Min(me.DonDeck.Count, 10 - me.CostArea.Count));
        int activeCount = await ChooseCount(ctx, "选择追加的活跃咚!!数量", activeMax);
        AtomicOps.RefreshDonFromDeck(me, activeCount, DonState.Active);

        int restMax = Math.Min(4, Math.Min(me.DonDeck.Count, 10 - me.CostArea.Count));
        int restCount = await ChooseCount(ctx, "选择追加的休息咚!!数量", restMax);
        AtomicOps.RefreshDonFromDeck(me, restCount, DonState.Rest);
        me.TurnOnceUsed.Add(key);

        // ② 之后：赋予我方 1 张角色最多 4 张休息状态的咚!!（从费用区休息咚附着）
        int attachMax = Math.Min(4, me.CostArea.Count(d => d.State == DonState.Rest));
        if (me.Characters.Count == 0 || attachMax <= 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "艾尼路：选择我方 1 张角色，赋予其最多 4 张休息状态的咚!!",
            me.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = me.Characters.First(c => c.Id.ToString() == chosen[0]);
        int attachCount = await ChooseCount(ctx, "选择赋予该角色的休息咚!!数量", attachMax);
        AtomicOps.AttachDonFromCost(me, target.Id, attachCount, DonState.Rest);
    }

    private static async Task<int> ChooseCount(EffectContext ctx, string prompt, int max)
    {
        if (max <= 0) return 0;
        var options = Enumerable.Range(0, max + 1).Select(n => $"{n} 张").ToList();
        int selected = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, prompt, options);
        return Math.Clamp(selected, 0, max);
    }
}
