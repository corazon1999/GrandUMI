using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-119 斩・切・麻糬（事件）
/// 【主要】我方的生命卡牌少于对方的场合，将对方最多 1 张费用不高于 4 的角色 KO。
///
/// 说明 / 简化点：
///   - 条件：我方生命数 < 对方生命数。
///   - 用当前费用 CurrentCostOf 判断"费用不高于 4"。
///   - 【触发】另行由触发关键词处理，不在此实现。
/// </summary>
public class OP03_119_SliceCutMochi : IScriptedEffect
{
    public string CardNumber => "OP03-119";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        if (me.LifeCount >= opp.LifeCount) return;

        var cands = opp.Characters
            .Where(c => ctx.State.CurrentCostOf(oppIdx, c) <= 4)
            .ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方 1 张费用≤4 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, oppIdx, tgt);
        }
    }
}
