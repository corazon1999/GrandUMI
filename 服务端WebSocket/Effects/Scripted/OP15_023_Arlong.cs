using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-023 阿龙（角色）
/// 【KO时】对方最多2张休息状态的卡牌，在下个对方的重置阶段中不会转为活跃状态。
/// 【启动主要】【每回合1次】可以赋予对方1张角色1张对方休息状态的咚!!：
///   赋予1张领袖或角色最多1张其持有者的费用区中的咚!!。
///
/// 实现说明 / 简化点：
///   - 【KO时】用 AtomicOps.PreventActivateNextReset 标记对方休息角色不转活跃，最多2张。
///     （引擎的重置阶段仅对"角色"尊重 CannotActivateNextReset 标记，领袖/舞台会无条件转活跃，
///      故此处目标限定为对方休息状态的角色，与实战绝大多数场景一致。）
///   - 【启动主要】成本"贴对方休息咚到对方角色"无 DSL cost 通道，脚本实现；收益赋予领袖/角色最多1张
///     其持有者费用区中的咚!!，卡文未限定活跃/休息状态。
///   - 成本需对方有休息咚且有角色，否则不发动；每回合1次。
/// </summary>
public class OP15_023_Arlong : IScriptedEffect
{
    public string CardNumber => "OP15-023";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnKO || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            // 对方最多2张休息状态的卡牌：领袖、角色、舞台或咚!!
            var restedCards = new List<CardInstance>();
            if (opp.Leader.IsTapped) restedCards.Add(opp.Leader);
            restedCards.AddRange(opp.Characters.Where(c => c.IsTapped));
            restedCards.AddRange(opp.StageCards.Where(stage => stage.IsTapped));
            var restedDons = opp.CostArea.Where(don => don.State == DonState.Rest).ToList();
            if (restedCards.Count + restedDons.Count == 0) return;

            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentRestingCardOrDon",
                "选择对方最多2张休息状态的卡牌，使其在下个对方重置阶段不转为活跃",
                restedCards.Select(c => c.Id.ToString()).Concat(restedDons.Select(d => d.Id.ToString())).ToList(), 0, 2,
                new Dictionary<string, object?>
                {
                    ["choiceCards"] = restedCards.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                    ["donChoices"] = restedDons.Select(d => new { id = d.Id.ToString(), state = d.State.ToString() }).ToList(),
                });
            foreach (var id in pick)
            {
                var card = restedCards.FirstOrDefault(c => c.Id.ToString() == id);
                if (card is not null) AtomicOps.PreventActivateNextReset(card);
                var don = restedDons.FirstOrDefault(d => d.Id.ToString() == id);
                if (don is not null) don.CannotActivateNextReset = true;
            }
            return;
        }

        // ── ActivatedMain ──
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        if (opp.RestDonCount < 1 || opp.Characters.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "阿龙【启动主要】：赋予对方1张角色1张对方休息咚作为成本；之后赋予1张领袖或角色最多1张其持有者费用区的咚!!？");
        if (!use) return;

        // 成本：赋予对方1张角色1张对方休息状态的咚!!
        var costPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "成本：选择1张对方角色，赋予1张对方休息状态的咚!!",
            opp.Characters.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (costPick.Count < 1) return;
        var costTarget = opp.Characters.FirstOrDefault(c => c.Id.ToString() == costPick[0]);
        if (costTarget is null) return;
        int paid = AtomicOps.AttachDonFromCost(opp, costTarget.Id, 1, DonState.Rest);
        if (paid < 1) return;

        me.TurnOnceUsed.Add(key);

        // 收益：赋予1张领袖或角色最多1张其持有者费用区中的咚!!（活跃或休息均可）
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        targets.Add(opp.Leader);
        targets.AddRange(opp.Characters);

        var pick2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LeaderOrCharacter",
            "选择1张领袖或角色，赋予最多1张其持有者费用区中的咚!!",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick2.Count < 1) return;
        var selectedTargetId = pick2[0];
        var selectedSnapshot = targets.FirstOrDefault(card => card.Id.ToString() == selectedTargetId);
        if (selectedSnapshot is null) return;
        bool expectedOwnHolder = ReferenceEquals(me.Leader, selectedSnapshot) || me.Characters.Contains(selectedSnapshot);
        var holder = expectedOwnHolder ? me : opp;
        var target = ReferenceEquals(holder.Leader, selectedSnapshot)
            ? holder.Leader
            : holder.Characters.FirstOrDefault(card => ReferenceEquals(card, selectedSnapshot));
        if (target is null) return;

        var availableDon = holder.CostArea
            .Where(don => (don.State is DonState.Active or DonState.Rest) && don.AttachedToCardId is null)
            .ToList();
        if (availableDon.Count == 0) return;
        // 保留既有 kind，避免改变客户端与回放所见的提示协议；候选状态由 donChoices 明确下发。
        var donPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "HolderActiveDon",
            "选择目标持有者费用区中最多1张咚!!赋予该目标",
            availableDon.Select(don => don.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?>
            {
                ["donChoices"] = availableDon.Select(don => new { id = don.Id.ToString(), state = don.State.ToString() }).ToList(),
            });
        if (donPick.Count < 1) return;

        // 目标与咚都按响应时的实际实例、持有者、区域和状态重新校验。
        bool targetStillHeld = ReferenceEquals(holder.Leader, target) || holder.Characters.Contains(target);
        var selectedDon = holder.CostArea.FirstOrDefault(don =>
            don.Id.ToString() == donPick[0]
            && (don.State is DonState.Active or DonState.Rest)
            && don.AttachedToCardId is null);
        if (!targetStillHeld || selectedDon is null) return;
        selectedDon.State = DonState.Attached;
        selectedDon.AttachedToCardId = target.Id;
    }
}
