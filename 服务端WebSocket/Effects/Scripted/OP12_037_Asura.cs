using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-037 鬼气 九刀流 阿修罗 拔剑 亡者游戏（事件）
/// 【主要】可以将我方的 3 张咚!! 转为休息状态：将对方合计最多 2 张角色或咚!! 转为休息状态。
/// 【反击】本次战斗中，我方领袖力量 +3000。
///
/// 说明：
///   - "将我方的 3 张咚!! 转为休息状态" 是发动【主要】的可选额外成本（需 ≥3 张活跃咚）。
///     与事件本身的印刷费用（1 费，由引擎在打出事件时支付）相互独立。
///   - 目标为"对方角色或咚!!"，合计最多 2 张：玩家可任意分配在对方角色与对方活跃咚上。
///     角色与咚使用实例 ID 放进同一个选择提示，咚的展示信息通过 donChoices 下发。
/// </summary>
public class OP12_037_Asura : IScriptedEffect
{
    public string CardNumber => "OP12-037";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 【反击】只增加我方领袖力量；即使当前被攻击的是角色，也不能把 +3000 转给角色。
            AtomicOps.AddLeaderPowerThisBattle(ctx.State, ctx.OwnerIndex, 3000);
            return;
        }

        // ── 【主要】 ──
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 可选额外成本：将我方 3 张活跃咚转为休息
        if (me.ActiveDonCount < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿修罗【主要】：将我方 3 张咚!! 转为休息状态，使对方合计最多 2 张角色或咚!! 转为休息？");
        if (!use) return;

        // 支付成本：3 张活跃咚 → 休息
        int rested = 0;
        foreach (var d in me.CostArea)
        {
            if (rested >= 3) break;
            if (d.State == DonState.Active) { d.State = DonState.Rest; rested++; }
        }
        if (rested < 3) return; // 理论上不会发生

        // 效果：从对方可被横置的活跃角色与活跃咚中，合计选择最多 2 张。
        // 角色和咚必须处于同一个提示中，玩家才能自由组合，而不是先选角色后自动处理咚。
        bool CanChooseCharacter(CardInstance card)
            => !card.IsTapped
               && !card.HasRestriction(RestrictionKind.CannotBeChosen)
               && !card.HasRestriction(RestrictionKind.CannotBeRested)
               && !ctx.State.HasContinuousRestriction(card, RestrictionKind.CannotBeChosen)
               && !ctx.State.HasContinuousRestriction(card, RestrictionKind.CannotBeRested);

        var activeCharacters = opp.Characters.Where(CanChooseCharacter).ToList();
        var activeDons = opp.CostArea.Where(don => don.State == DonState.Active).ToList();
        var validChoices = activeCharacters.Select(card => card.Id.ToString())
            .Concat(activeDons.Select(don => don.Id.ToString()))
            .ToList();
        if (validChoices.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterOrDon",
            "选择对方最多 2 张活跃角色或活跃咚!!转为休息状态",
            validChoices, 0, 2,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = activeCharacters.Select(card => new
                {
                    id = card.Id.ToString(),
                    number = card.Info.Number,
                }).ToList(),
                ["donChoices"] = activeDons.Select(don => new
                {
                    id = don.Id.ToString(),
                    state = don.State.ToString(),
                }).ToList(),
            });

        foreach (var id in chosen)
        {
            var character = activeCharacters.FirstOrDefault(card => card.Id.ToString() == id);
            if (character is not null)
            {
                AtomicOps.RestCard(character);
                continue;
            }

            var don = activeDons.FirstOrDefault(candidate => candidate.Id.ToString() == id);
            if (don is not null && don.State == DonState.Active) don.State = DonState.Rest;
        }
    }
}
