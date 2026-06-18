using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-004 爱德华·纽哥特（角色 / 炎 / 9费）
/// 【登场时】直到下个我方的回合开始时为止，我方最多1张领袖力量+2000。之后，本回合中，我方无法通过我方的效果
///   将生命卡牌加入手牌。
/// 【咚!!×2】【攻击时】将对方最多1张力量不高于3000的角色KO。
///
/// 实现：OnEnterField（领袖+2000 持续到下个我方回合 + 置 NoEffectLifeToHandThisTurn）+ OnAttackDeclare（咚≥2 时 KO）。
/// </summary>
public class OP02_004_Whitebeard : IScriptedEffect
{
    public string CardNumber => "OP02-004";
    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "纽哥特：将我方领袖力量+2000（直到下个我方回合开始）？");
            if (use)
            {
                int owner = ctx.OwnerIndex;
                var lid = me.Leader.Id;
                int regTurn = ctx.State.TurnCount;
                ctx.State.ContinuousEffects.Add(new ContinuousEffect
                {
                    SourceCardId = self.Id.ToString(),
                    Scope = new ContinuousScope { Side = 0, IncludeLeader = true, IncludeCharacters = false },
                    PowerDelta = 2000,
                    Predicate = (s, sideIdx, c) => c.Id == lid && s.TurnCount <= regTurn + 1,
                });
            }
            // 之后：本回合我方无法通过效果将生命卡牌加入手牌
            ctx.State.NoEffectLifeToHandThisTurn.Add(ctx.OwnerIndex);
            return;
        }

        // OnAttackDeclare：【咚!!×2】KO对方最多1张力量≤3000角色
        if (me.AttachedDonCount(self.Id) < 2) return;
        var cands = opp.Characters.Where(c => ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, c) <= 3000).ToList();
        if (cands.Count == 0) return;
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多1张力量≤3000的角色KO", cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (ch.Count > 0)
        {
            var tgt = cands.FirstOrDefault(c => c.Id.ToString() == ch[0]);
            if (tgt is not null) AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
