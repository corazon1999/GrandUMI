using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-063 凯波特（角色）
/// 【登场时】咚!!-1（可以将我方场上指定数量的咚‼放回咚‼卡组）：我方领袖拥有《福克斯海盗团》特征
///   的场合，直到下个对方的回合结束时为止，对方最多 1 张费用不高于 6 的角色无法攻击。
///
/// 实现说明 / 简化点：
///   - 发动成本"咚!!-1"用 ReturnDonToDeck(me, 1) 表达；先 ConfirmOptional 询问"可以"。
///   - 前提：我方领袖拥有《福克斯海盗团》特征，否则无收益不提示。
///   - "无法攻击（直到下个对方回合结束）"用 AddRestriction(CannotAttack, UntilNextOpponentEndPhase)。
///   - 费用按 CurrentCost() 评估。
/// </summary>
public class OP07_063_Capote : IScriptedEffect
{
    public string CardNumber => "OP07-063";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 前提：我方领袖拥有《福克斯海盗团》特征
        if (me.Leader is null || !me.Leader.Info.HasKeyword("福克斯海盗团")) return;

        // 需要可被支付的咚!!-1 成本与可被指定的目标
        if (me.TotalDonInCostArea < 1) return;
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 6).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "凯波特【登场时】：咚!!-1，使对方最多 1 张费用≤6 的角色无法攻击（直到下个对方回合结束）？");
        if (!use) return;

        // 成本：咚!!-1
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择最多 1 张费用≤6 的对方角色，使其无法攻击",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddRestriction(tgt, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase);
        }
    }
}
