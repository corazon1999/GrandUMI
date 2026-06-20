using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-074 泽法（角色）
/// 【登场时】咚!!1（可以将我方场上指定数量的咚‼放回咚‼卡组）：
///   本回合中，对方最多 1 张角色的效果无效。
///   之后，该角色的力量不高于 5000 的场合，将其 KO。
///
/// 说明 / 简化点：
///   - 成本"咚!!1"为可选支付（可以将 1 张咚放回咚卡组）。仅当费用区有咚可放回时询问发动。
///   - 效果作用于同一张选中的对方角色：先使其本回合效果无效，再判定其当前力量；
///     力量不高于 5000 则 KO。力量评估用含持续效果的 CurrentPowerOf。
/// </summary>
public class OP06_074_Zephyr : IScriptedEffect
{
    public string CardNumber => "OP06-074";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (opp.Characters.Count == 0) return;

        // 成本：咚!!1（可以将 1 张咚放回咚卡组）—— 无可放回的咚则无法发动
        if (me.TotalDonInCostArea < 1) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "泽法【登场时】：将我方 1 张咚!! 放回咚!! 卡组，使对方 1 张角色本回合效果无效，并在其力量≤5000 时将其KO？");
        if (!use) return;

        // 支付成本：1 张咚放回咚卡组
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        // 选择对方最多 1 张角色
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，使其本回合效果无效",
            opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = opp.Characters.First(c => c.Id.ToString() == chosen[0]);

        // 本回合中，该角色的效果无效
        AtomicOps.NullifyEffects(target, KeywordDuration.ThisTurn);

        // 之后：该角色力量不高于 5000 的场合，将其 KO
        if (ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, target) <= 5000)
        {
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, target);
        }
    }
}
