using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-018 梅迦罗（角色 / 风 / 动物·鱼人岛）
/// 【登场时】可以将此角色转为休息状态：将对方最多 1 张处于休息状态且力量不高于 8000 的角色KO。
///
/// 实现说明：
///   - 可选成本：将此角色转为休息状态（RestCard）。
///   - 目标：对方处于休息状态（IsTapped）且当前力量≤8000 的角色；力量用 CurrentPowerOf（含持续修正）。
///   - KO 用 AtomicOps.KO(state, 对方下标, 目标)。
/// </summary>
public class EB04_018_Megalo : IScriptedEffect
{
    public string CardNumber => "EB04-018";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;
        int oppIdx = 1 - ctx.OwnerIndex;

        var cands = opp.Characters
            .Where(c => c.IsTapped && ctx.State.CurrentPowerOf(oppIdx, c) <= 8000)
            .ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "梅迦罗【登场时】：将此角色转为休息状态，KO 对方最多 1 张休息状态且力量≤8000 的角色？");
        if (!use) return;

        // 成本：将此角色转为休息状态
        AtomicOps.RestCard(self);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张休息状态且力量≤8000 的角色KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, oppIdx, tgt);
        }
    }
}
