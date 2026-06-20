using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-100 卡彭·班吉（角色 / 光 / 火焰坦克海盗团）
/// 【触发】本回合中，对方最多 1 张领袖或角色无法攻击。
///
/// 实现说明：
///   - 触发(OnLifeRevealTrigger)：选对方最多 1 张领袖或角色，施加 CannotAttack(本回合)限制。
///   - ActionValidator 在攻击校验时检查 RestrictionKind.CannotAttack。
/// </summary>
public class OP04_100_CaponBege : IScriptedEffect
{
    public string CardNumber => "OP04-100";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var targets = new List<CardInstance> { opp.Leader };
        targets.AddRange(opp.Characters);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "选择对方最多 1 张领袖或角色，本回合中无法攻击",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddRestriction(tgt, RestrictionKind.CannotAttack, KeywordDuration.ThisTurn);
        }
    }
}
