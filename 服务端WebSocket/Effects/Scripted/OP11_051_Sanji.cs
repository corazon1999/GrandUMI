using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-051 山智（角色 / 温思默克家·草帽一伙）
/// 当此角色因对方的效果而被KO时，确认我方卡组最上方的 5 张，将其中最多 1 张费用≤5 且
///   拥有《草帽一伙》特征的角色登场，剩余自选顺序放回卡组最下方。 → complex（见下）
/// 【登场时】将最多 1 张原本的力量不高于 5000 的角色放回其持有者的手牌。 → 本脚本实现此段
///
/// 实现说明 / 简化点：
///   - 仅实现【登场时】段：将场上（双方）最多 1 张原本力量（c.Info.Power）≤5000 的角色退回其持有者手牌。
///     可选最多 1 张（min=0）。退回时按目标所属方调用 BounceToHand。
///   - "因对方效果被KO时"的 OnKO 段：触发来源限定（是否因对方效果）无对应钩子区分，OnKO 无法表达，
///     该段省略。
/// </summary>
public class OP11_051_Sanji : IScriptedEffect
{
    public string CardNumber => "OP11-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 候选：双方场上原本力量≤5000 的角色
        var mine = me.Characters.Where(c => c.Info.Power <= 5000).ToList();
        var theirs = opp.Characters.Where(c => c.Info.Power <= 5000).ToList();
        var cands = new List<CardInstance>();
        cands.AddRange(mine);
        cands.AddRange(theirs);
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "AnyCharacter",
            "选最多 1 张原本力量≤5000 的角色，放回其持有者手牌",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = cands.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is null) return;

        // 按目标所属方退回手牌
        int ownerOfTarget = mine.Any(c => c.Id == target.Id) ? ctx.OwnerIndex : 1 - ctx.OwnerIndex;
        AtomicOps.BounceToHand(ctx.State, ownerOfTarget, target);
    }
}
