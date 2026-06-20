using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-017 混乱空间（事件）
/// 【主要】选择对方 2 张原本的力量不高于 9000 的角色。
///   本回合中，将所选角色各自原本的力量互换。
///
/// 实现说明 / 简化点：
///   - "原本的力量"取卡面原始力量 Info.Power。
///   - 互换通过对两张角色各自施加 ThisTurn 力量差值实现：
///     A += (B原力 - A原力)，B += (A原力 - B原力)。
///   - 候选必须恰好选择 2 张；不足 2 张候选则无法发动。
/// </summary>
public class OP14_017_ChaosSpace : IScriptedEffect
{
    public string CardNumber => "OP14-017";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var cands = opp.Characters.Where(c => c.Info.Power <= 9000).ToList();
        if (cands.Count < 2) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方 2 张原本力量≤9000 的角色，互换其原本力量",
            cands.Select(c => c.Id.ToString()).ToList(), 2, 2);
        if (chosen.Count < 2) return;

        var a = cands.First(c => c.Id.ToString() == chosen[0]);
        var b = cands.First(c => c.Id.ToString() == chosen[1]);

        int aBase = a.Info.Power;
        int bBase = b.Info.Power;

        // 互换原本力量：本回合中各自加上差值
        AtomicOps.AddPowerThisTurn(a, bBase - aBase);
        AtomicOps.AddPowerThisTurn(b, aBase - bBase);
    }
}
