using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-093 斯摩格（领航 / 领袖）
/// 【咚!!×1】【启动主要】【每回合1次】本回合中，对方最多 1 张角色费用-1。
/// 之后，场上存在费用为 0 的角色的场合，本回合中，此领袖力量+1000。
///
/// 实现说明：
///   - 每回合 1 次用 TurnOnceUsed 控制。
///   - 对方角色费用-1（本回合）用 AtomicOps.AddCostModifier(c,-1,ThisTurn)。
///   - "之后场上存在费用为 0 的角色" 用 CurrentCostOf 判定双方场上任意角色当前费用是否为 0；
///     成立则给领袖本回合 +1000（AddPowerThisTurn）。
///   - 【咚!!×1】激活条件由引擎/动作校验处理，此处实现效果本体。
/// </summary>
public class OP02_093_Smoker : IScriptedEffect
{
    public string CardNumber => "OP02-093";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var key = "OP02-093-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);

        // 对方最多 1 张角色费用-1（本回合）
        if (opp.Characters.Count > 0)
        {
            var ids = opp.Characters.Select(c => c.Id.ToString()).ToList();
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张对方角色，本回合中费用-1", ids, 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = opp.Characters.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddCostModifier(tgt, -1, KeywordDuration.ThisTurn);
            }
        }

        // 之后：场上存在费用为 0 的角色 → 此领袖本回合 +1000
        bool hasZeroCost =
            me.Characters.Any(c => ctx.State.CurrentCostOf(ctx.OwnerIndex, c) == 0) ||
            opp.Characters.Any(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) == 0);
        if (hasZeroCost)
        {
            AtomicOps.AddPowerThisTurn(me.Leader, 1000);
        }
    }
}
