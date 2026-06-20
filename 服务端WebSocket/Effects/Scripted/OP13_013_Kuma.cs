using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-013 日熊（1 费 3000，山贼）
/// 【登场时】将对方最多 1 张力量不高于 0 的角色 KO。
///
/// 实现：登场时筛选对方场上"当前力量 ≤ 0"的角色（含临时修正/咚加成；
/// 由于此时是我方回合，对方角色不享有自身咚的攻击加成，ownerTurn=false），
/// 让我方玩家选择最多 1 张 KO（min=0，可不选）。
/// </summary>
public class OP13_013_Kuma : IScriptedEffect
{
    public string CardNumber => "OP13-013";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = s.Players[oppIdx];
        bool oppOwnerTurn = s.CurrentTurnPlayer == oppIdx;

        var candidates = opp.Characters
            .Where(c => c.CurrentPower(opp.AttachedDonCount(c.Id), oppOwnerTurn) <= 0)
            .ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张力量不高于 0 的角色 KO",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.KO(s, oppIdx, target);
    }
}
