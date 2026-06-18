using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-009 妮古·罗宾（角色）
/// 【咚!!×2】【攻击时】直到下个对方的回合结束时为止，对方最多 1 张角色力量 -2000。
///
/// 实现说明 / 简化点：
///   - 【咚!!×2】为发动条件：此角色需被赋予 2 张或更多咚!!。
///   - 力量修正引擎仅提供 ThisTurn / ThisBattle / Persistent 三档通道，无"直到下个对方回合结束"
///     的力量时长通道，故用 AddPowerThisTurn 近似（在本回合内生效）。属时长上的轻微简化。
/// </summary>
public class OP11_009_Robin : IScriptedEffect
{
    public string CardNumber => "OP11-009";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×2】条件：自身被赋予 2 张或更多咚
        if (me.AttachedDonCount(self.Id) < 2) return;

        var cands = opp.Characters.ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择最多1张对方角色，使其力量-2000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, -2000);
        }
    }
}
