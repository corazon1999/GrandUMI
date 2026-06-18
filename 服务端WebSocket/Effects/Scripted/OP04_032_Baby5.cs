using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-032 Baby 5（角色）
/// 【我方的回合结束时】可以将此角色放置到废弃区：将我方最多 2 张咚!! 转为活跃状态。
///
/// 实现说明：
///   - 【我方的回合结束时】用 OnMyTurnEnd；"可以"用 ConfirmOptional 询问。
///   - 成本"将此角色放置废弃区"用 BattleEngine.KOCard(自我牺牲，绕过防KO光环)。
///   - 收益：将我方最多 2 张休息状态的咚!! 转为活跃状态。
/// </summary>
public class OP04_032_Baby5 : IScriptedEffect
{
    public string CardNumber => "OP04-032";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnMyTurnEnd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 有休息咚才有收益，否则无意义
        var restDons = me.CostArea.Where(d => d.State == DonState.Rest).ToList();
        if (restDons.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "Baby 5【我方的回合结束时】：将此角色放置废弃区，将我方最多 2 张咚!! 转为活跃状态？");
        if (!use) return;

        // 成本：将此角色放置到废弃区（自我牺牲）
        BattleEngine.KOCard(ctx.State, ctx.OwnerIndex, self);

        // 收益：将我方最多 2 张休息咚转为活跃
        foreach (var d in restDons.Take(2))
            d.State = DonState.Active;
    }
}
