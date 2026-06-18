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

        // ① 从咚!!卡组追加：最多 1 活跃 + 最多 4 休息（受咚!!卡组余量与上限约束）
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        AtomicOps.RefreshDonFromDeck(me, 4, DonState.Rest);
        me.TurnOnceUsed.Add(key);

        // ② 之后：赋予我方 1 张角色最多 4 张休息状态的咚!!（从费用区休息咚附着）
        if (me.Characters.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "艾尼路：选择我方 1 张角色，赋予其最多 4 张休息状态的咚!!",
            me.Characters.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count == 0) return;

        var target = me.Characters.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AttachDonFromCost(me, target.Id, 4, DonState.Rest);
    }
}
