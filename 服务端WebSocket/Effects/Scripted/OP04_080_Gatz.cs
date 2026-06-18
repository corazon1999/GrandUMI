using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-080 盖茨（角色 / 地 / 德莱斯罗兹）
/// 【登场时】本回合中，我方最多 1 张拥有《德莱斯罗兹》特征的角色也可以攻击处于活跃状态的角色。
///
/// 实现说明：
///   - "可攻击活跃状态角色" 用 GiveKeyword(c, "可攻击活跃", ThisTurn)，ActionValidator 据此放行。
///   - 候选 = 我方拥有《德莱斯罗兹》特征的角色（含本卡）。
/// </summary>
public class OP04_080_Gatz : IScriptedEffect
{
    public string CardNumber => "OP04-080";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Characters.Where(c => c.Info.HasKeyword("德莱斯罗兹")).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "本回合中，选择最多 1 张《德莱斯罗兹》角色，使其也可以攻击活跃状态的角色",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.GiveKeyword(tgt, "可攻击活跃", KeywordDuration.ThisTurn);
        }
    }
}
