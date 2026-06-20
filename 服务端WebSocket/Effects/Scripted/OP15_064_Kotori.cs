using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-064 小鸟（暗 1 费 2000，空岛）
/// 【启动主要】咚!!-2，可以将此角色转为休息状态：
///   我方场上存在"阿悟"和"小边"的场合，将对方最多 1 张力量不高于 5000 的角色转为休息状态。
///
/// 实现说明：
///   - 成本：咚!!-2（ReturnDonToDeck 2）+ 将此角色转为休息状态（RestCard）。"可以"= 可选，先 ConfirmOptional。
///   - 前置条件："我方场上存在《阿悟》和《小边》"用 MatchesName 在我方角色区按名称判定。
///   - 自身需处于活跃状态才能支付横置成本；费用区需有 ≥2 张可放回的咚。
///   - 收益："力量不高于 5000"取当前力量 CurrentPowerOf；将对方所选角色转为休息状态。
/// </summary>
public class OP15_064_Kotori : IScriptedEffect
{
    public string CardNumber => "OP15-064";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 前置条件：我方场上同时存在"阿悟"和"小边"
        bool agu = me.Characters.Any(c => c.MatchesName("阿悟"));
        bool kobe = me.Characters.Any(c => c.MatchesName("小边"));
        if (!agu || !kobe) return;

        // 成本可支付性：自身需活跃；费用区需 ≥2 张可放回的咚（活跃/休息/附着皆可）
        if (self.IsTapped) return;
        if (me.CostArea.Count < 2) return;

        // 收益候选：对方力量≤5000 的角色
        var candidates = opp.Characters
            .Where(c => s.CurrentPowerOf(1 - ctx.OwnerIndex, c) <= 5000)
            .ToList();
        if (candidates.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "小鸟【启动主要】：支付『咚!!-2』并将此角色转为休息状态，将对方最多 1 张力量≤5000 的角色转为休息状态？");
        if (!use) return;

        // 成本：咚!!-2 + 横置自身
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 2)) return;
        AtomicOps.RestCard(self);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterPwLe5000",
            "选择对方最多 1 张力量≤5000 的角色转为休息状态",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.RestCard(target);
    }
}
