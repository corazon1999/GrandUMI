using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-086 特拉法尔加·罗（领袖）
/// 【启动主要】【每回合1次】咚!!-3，可以将我方1张力量为3000或更高的角色放回卡组最下方：
///   将我方手牌中最多1张费用不高于4且拥有《心脏海盗团》特征的角色卡牌登场。
///
/// 实现说明：
///   - 成本：咚!!-3（ReturnDonToDeck）+ 可选「将我方力量≥3000角色放回卡组底」(ConfirmOptional+选卡)。
///   - 力量判定用 ctx.State.CurrentPowerOf 取含持续效果的当前力量。
///   - 收益：从手牌登场最多1张费用≤4且《心脏海盗团》的角色。
/// </summary>
public class P_086_TrafalgarLaw : IScriptedEffect
{
    public string CardNumber => "P-086";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 每回合1次
        var key = "P-086-act";
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本前置：费用区需要至少3张可放回的咚（活跃/休息/附着皆可）
        if (me.CostArea.Count < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "罗【启动主要】：咚!!-3，并可将我方1张力量≥3000的角色放回卡组底，登场1张费用≤4的《心脏海盗团》角色？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        // 成本：咚!!-3
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 3)) return;

        // 可选成本：将我方1张力量≥3000的角色放回卡组最下方
        var charCands = me.Characters
            .Where(c => ctx.State.CurrentPowerOf(ctx.OwnerIndex, c) >= 3000)
            .ToList();
        if (charCands.Count > 0)
        {
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "可将我方1张力量≥3000的角色放回卡组最下方（可不选）",
                charCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count > 0)
            {
                var rb = charCands.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, rb);
            }
        }

        // 收益：从手牌登场最多1张费用≤4且《心脏海盗团》的角色
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost <= 4 &&
            c.Info.HasKeyword("心脏海盗团")).ToList();
        if (playable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场1张费用≤4的《心脏海盗团》角色（最多1张）",
            playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = playable.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
