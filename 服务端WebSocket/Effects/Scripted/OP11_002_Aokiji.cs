using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-002 艾恩（角色）
/// 【登场时】本回合中，对方最多 1 张角色力量 -1000。之后，将对方最多 1 张力量不高于 0 的角色 KO。
///
/// 实现说明 / 简化点：
///   - 先选择对方 1 张角色给予本回合 -1000；KO 判定使用 ctx.State.CurrentPowerOf（含本回合修正/持续效果），
///     从而能正确判定"被 -1000 后力量不高于 0"的角色（含连锁后的实时力量）。
///   - 两步均为"最多 1 张"，玩家可放弃（min=0）。
/// </summary>
public class OP11_002_Aokiji : IScriptedEffect
{
    public string CardNumber => "OP11-002";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        // 第一步：本回合中，对方最多 1 张角色力量 -1000
        var cands = opp.Characters.ToList();
        if (cands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张角色，本回合中力量 -1000",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddPowerThisTurn(tgt, -1000);
            }
        }

        // 第二步：将对方最多 1 张力量不高于 0 的角色 KO（按当前实时力量判定）
        var koCands = opp.Characters.Where(c => ctx.State.CurrentPowerOf(oppIdx, c) <= 0).ToList();
        if (koCands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张力量不高于 0 的角色 KO",
                koCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = koCands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, oppIdx, tgt);
            }
        }
    }
}
