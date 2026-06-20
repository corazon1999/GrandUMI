using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-026 乔邦尼（角色）
/// 【咚!!×1】【攻击时】对方最多1张处于休息状态且费用不高于1的角色，
///   在下个对方的重置阶段中不会转为活跃状态。
///
/// 实现说明 / 简化点：
///   - 【咚!!×1】为发动条件：自身被赋予中的咚!! ≥1（即合计费用区附着到此卡的咚数 ≥1）。
///     用 me.AttachedDonCount(self.Id) ≥ 1 判定。
///   - 用 AtomicOps.PreventActivateNextReset 标记最多 1 张对方休息状态且 Info.Cost ≤ 1 的角色。
/// </summary>
public class OP08_026_Giovanni : IScriptedEffect
{
    public string CardNumber => "OP08-026";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】：自身被赋予中的咚!! 至少 1 张
        if (me.AttachedDonCount(self.Id) < 1) return;

        var cands = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 1).ToList();
        if (cands.Count == 0) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多1张休息状态且费用≤1的角色，使其在下个对方重置阶段不转为活跃",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        foreach (var id in pick)
        {
            var c = cands.First(x => x.Id.ToString() == id);
            AtomicOps.PreventActivateNextReset(c);
        }
    }
}
