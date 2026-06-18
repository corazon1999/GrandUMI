using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-018 霸王色的霸气（事件）
/// 【反击】本次战斗中，我方最多 1 张角色或"希尔巴兹·雷利"力量+2000。
///   之后，可以将我方的 1 张咚!!转为休息状态。那样做的场合，本回合中，对方的领袖和所有角色力量-1000。
///
/// 说明：
/// - "角色或希尔巴兹·雷利"——可选对象为我方所有角色，外加我方领袖（仅当其名为"希尔巴兹·雷利"）。
/// - "将我方的 1 张咚!!转为休息状态"实现为把 1 张活跃咚改为休息（无活跃咚则无法选择该追加项）。
/// </summary>
public class OP12_018_Haoshoku : IScriptedEffect
{
    public string CardNumber => "OP12-018";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ── 本次战斗中，我方最多 1 张角色或"希尔巴兹·雷利"力量+2000 ──
        var buffTargets = new List<CardInstance>(me.Characters);
        if (me.Leader.MatchesName("希尔巴兹·雷利")) buffTargets.Add(me.Leader);

        if (buffTargets.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharOrRayleigh",
                "选择最多 1 张角色或\"希尔巴兹·雷利\"，本次战斗力量+2000",
                buffTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var t = buffTargets.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddPowerThisBattle(t, 2000);
            }
        }

        // ── 之后，可以将我方的 1 张咚!!转为休息状态 → 对方领袖和所有角色本回合力量-1000 ──
        if (me.ActiveDonCount < 1) return;

        bool rest = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "霸王色的霸气：将 1 张咚!!转为休息状态，使对方领袖和所有角色本回合力量-1000？");
        if (!rest) return;

        // 把 1 张活跃咚转为休息
        var don = me.CostArea.FirstOrDefault(d => d.State == DonState.Active);
        if (don is null) return;
        don.State = DonState.Rest;

        AtomicOps.AddPowerToAllThisTurn(ctx.State, 1 - ctx.OwnerIndex, _ => true, -1000, includeLeader: true);
    }
}
