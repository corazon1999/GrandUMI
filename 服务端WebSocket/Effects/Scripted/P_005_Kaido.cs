using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-005 盖德（角色 / 暗 / 四皇・百兽海盗团，cost7 power8000）
/// 【启动主要】咚‼-2：本回合中，此角色获得【流放】效果。
///
/// 实现：可选启动主要。成本=将我方场上 2 张咚!! 放回咚!!卡组（ReturnDonToDeck，需活跃咚≥2）。
///   收益=本回合赋予自身【流放】（AtomicOps.GiveKeyword，ThisTurn；流放已被引擎消费）。
/// </summary>
public class P_005_Kaido : IScriptedEffect
{
    public string CardNumber => "P-005";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 成本：费用区需 2 张可放回咚!!卡组的咚（活跃/休息/附着皆可）
        if (me.CostArea.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "盖德【启动主要】：将 2 张咚!! 放回咚!!卡组，使此角色本回合获得【流放】？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 2)) return;
        AtomicOps.GiveKeyword(ctx.Source, "流放", KeywordDuration.ThisTurn);
    }
}
