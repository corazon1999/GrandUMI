using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-010 云雀（角色）
/// 【登场时】本回合中，对方最多 1 张角色力量 -2000。
/// 【攻击时】本回合中，此角色的力量 +1000。
///   之后，本回合中，我方最多 1 张拥有《海军》特征的领袖也可以攻击对方处于活跃状态的角色。
///
/// 实现说明 / 简化点：
///   - 【登场时】：让玩家选择最多 1 张对方角色，本回合 -2000（AddPowerThisTurn 负值）。
///   - 【攻击时】：此角色本回合 +1000（AddPowerThisTurn）。
///   - 【攻击时】结算力量后，若我方领袖拥有《海军》特征，可选择该领袖并赋予本回合
///     "可攻击活跃"关键词；攻击合法性校验会据此放行攻击对方活跃角色。
/// </summary>
public class OP11_010_Tashigi : IScriptedEffect
{
    public string CardNumber => "OP11-010";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var cands = opp.Characters.ToList();
            if (cands.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张对方角色，本回合力量 -2000",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddPowerThisTurn(tgt, -2000);
            }
            return;
        }

        // OnAttackDeclare：仅当此角色为本次攻击的攻击者时，本回合 +1000
        if (ctx.Trigger == EffectTrigger.OnAttackDeclare)
        {
            var b = ctx.State.CurrentBattle;
            if (b is null || b.AttackerCardId != self.Id) return;
            AtomicOps.AddPowerThisTurn(self, 1000);

            if (!me.Leader.Info.HasKeyword("海军")) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeader",
                "选择我方《海军》领袖，本回合可攻击对方活跃状态的角色",
                new[] { me.Leader.Id.ToString() }, 0, 1);
            if (chosen.Count > 0)
                AtomicOps.GiveKeyword(me.Leader, "可攻击活跃", KeywordDuration.ThisTurn, ctx.OwnerIndex);
        }
    }
}
