using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-040 橡皮橡皮铁锤回转弹（事件，风，因佩尔地狱/草帽一伙）
/// 【主要】我方场上存在"蒙奇·D·路飞"和"Mr.3(加尔迪诺)"的场合，对方最多 1 张处于休息状态且
///         费用不高于 6 的角色，在下个对方的重置阶段中不会转为活跃状态。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 实现说明：
///   - 条件需同时检测场上存在卡名"蒙奇·D·路飞"与"Mr.3(加尔迪诺)"两个角色，分别用 Any(MatchesName)。
///   - 满足条件后从对方休息状态且费用≤6 的角色中选最多 1 张，标记其下个重置阶段不会转活跃。
///   - 【反击】= 本次战斗领袖 +3000。
/// </summary>
public class OP16_040_GomuGomuNoHammerSpin : IScriptedEffect
{
    public string CardNumber => "OP16-040";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ── 【反击】 ──
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            AtomicOps.AddPowerThisBattle(me.Leader, 3000);
            return;
        }

        // ── 【主要】 ──
        // 条件：场上同时存在"蒙奇·D·路飞"和"Mr.3(加尔迪诺)"
        bool hasLuffy = me.Characters.Any(c => c.MatchesName("蒙奇·D·路飞"));
        bool hasMr3 = me.Characters.Any(c => c.MatchesName("Mr.3(加尔迪诺)"));
        if (!hasLuffy || !hasMr3) return;

        // 候选：对方处于休息状态且费用≤6 的角色
        var candidates = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 6).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCharacterCostLe6",
            "选择对方最多 1 张休息状态且费用不高于 6 的角色，使其在下个对方重置阶段不会转为活跃",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.PreventActivateNextReset(target);
    }
}
