using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-006 空扎（角色 / 炎 / 阿拉巴斯坦王国）
/// 【攻击时】本回合中，可以将我方 1 张活跃状态的领袖力量-5000：
///   直到下个我方的回合开始时为止，此角色的力量+2000。
///
/// 实现说明：
///   - 成本：本回合中将我方活跃状态的领袖力量-5000（AddPowerThisTurn(-5000)）。
///     仅当领袖处于活跃（未横置）时可支付，可选发动。
///   - 收益持续时长"直到下个我方回合开始"超出本回合 → 用 ContinuousEffect 注册，
///     Predicate 以发动时回合数为基准，覆盖本回合(T)与随后对方回合(T+1)，
///     到我方下个回合(T+2)开始失效。
/// </summary>
public class OP04_006_Kuzan : IScriptedEffect
{
    public string CardNumber => "OP04-006";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 领袖需为活跃状态才能作为成本被降力量
        if (me.Leader.IsTapped) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "空扎【攻击时】：将我方活跃领袖力量-5000，使此角色力量+2000(持续到下个我方回合开始)?");
        if (!use) return;

        // 成本：领袖本回合力量-5000
        AtomicOps.AddPowerThisTurn(me.Leader, -5000);

        // 收益：注册持续 +2000，覆盖本回合与随后的对方回合
        int regTurn = ctx.State.TurnCount;
        var selfId = self.Id;
        int owner = ctx.OwnerIndex;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            Predicate = (s, sideIdx, c) =>
                sideIdx == owner && c.Id == selfId && s.TurnCount <= regTurn + 1,
        });
    }
}
