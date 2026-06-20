using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-039 就凭你连给我解闷都不配！！！（事件）
/// 【主要】选择以下的 1 项：
///   ・将对方最多 1 张费用不高于 6 的角色转为休息状态。
///   ・将对方最多 1 张处于休息状态且费用不高于 6 的角色 KO。
/// 用 ChooseOption 实现"选择以下 1 项"的多选一分支。
/// </summary>
public class OP06_039_JustYou : IScriptedEffect
{
    public string CardNumber => "OP06-039";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择以下的 1 项",
            new[] { "将对方 1 张费用≤6 的角色转为休息状态", "将对方 1 张休息状态且费用≤6 的角色 KO" });

        if (opt == 0)
        {
            var cands = opp.Characters.Where(c => c.Info.Cost <= 6).ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张费用≤6 的对方角色转为休息状态",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.RestCard(tgt);
            }
        }
        else
        {
            var cands = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 6).ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张处于休息状态且费用≤6 的对方角色 KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }
    }
}
