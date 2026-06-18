using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-024 糖糖（角色 / 风 / 0力）
/// 【对方的回合中】【每回合1次】当对方将角色登场时，我方领袖拥有《堂吉诃德海盗团》特征的场合，
///   将对方最多1张角色转为休息状态。之后，将此角色转为休息状态。
/// 【登场时】将对方最多1张费用不高于4的角色转为休息状态。
///
/// 实现：OnEnterField（登场时休息≤4费对方角色）+ OnAllyCharEnter（监听对方角色登场，owner!=自己）。
/// </summary>
public class OP04_024_Sugar : IScriptedEffect
{
    public string CardNumber => "OP04-024";
    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAllyCharEnter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 【登场时】对方最多1张费用≤4角色转休息
            var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 4).ToList();
            if (cands.Count == 0) return;
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多1张费用≤4的角色转为休息状态",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0)
            {
                var t = cands.FirstOrDefault(c => c.Id.ToString() == ch[0]);
                if (t is not null) AtomicOps.RestCard(t);
            }
            return;
        }

        // OnAllyCharEnter：当对方将角色登场时
        var owner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (owner == ctx.OwnerIndex) return;                                            // 须为对方角色登场
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;                      // 须对方回合中
        if (!me.Leader.Info.HasKeyword("堂吉诃德海盗团")) return;
        var key = "OP04-024-oppenter" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;                                      // 每回合1次

        if (opp.Characters.Count > 0)
        {
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多1张角色转为休息状态",
                opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0)
            {
                var t = opp.Characters.FirstOrDefault(c => c.Id.ToString() == ch[0]);
                if (t is not null) AtomicOps.RestCard(t);
            }
        }
        me.TurnOnceUsed.Add(key);
        AtomicOps.RestCard(self);   // 之后将此角色转为休息状态
    }
}
