using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-051 鼻哼三步『箭羽斩』（事件 / 地 3 费）
/// 【主要】选择以下的 1 项。
///   ・将对方最多 1 张费用不高于 2 的角色 KO。
///   ・本回合中，对方最多 1 张角色费用 -4。
///
/// 实现说明：
///   - 二选一用 ChooseOption 返回下标后分支。
///   - 选项 1：候选为对方费用≤2 角色，玩家选 0..1 张 KO。
///   - 选项 2：本回合对方最多 1 张角色费用 -4，用 AddCostModifier(-4, ThisTurn)。
/// </summary>
public class EB02_051_BoxBaneZan : IScriptedEffect
{
    public string CardNumber => "EB02-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择以下的 1 项",
            new[] { "将对方最多 1 张费用≤2 的角色 KO", "本回合中，对方最多 1 张角色费用 -4" });

        if (opt == 0)
        {
            var cands = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张费用≤2 的角色 KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }
        else
        {
            var cands = opp.Characters.ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "本回合中，对方最多 1 张角色费用 -4",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddCostModifier(tgt, -4, KeywordDuration.ThisTurn);
            }
        }
    }
}
