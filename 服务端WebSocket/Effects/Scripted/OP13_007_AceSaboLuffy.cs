using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-007 艾斯&amp;萨波&amp;路飞（1 费 1000）
/// 【启动主要】可以赋予我方领袖或 1 张角色 1 张我方处于活跃状态的咚!!，并将此角色放置到废弃区：
///   本回合中，对方最多 1 张角色力量 -3000。
///
/// 说明（整体为可选效果"可以…"）：
///   - 代价 = 把 1 张活跃咚赋予我方领袖或 1 张角色 + 将此角色（自身）放置到废弃区。
///   - 若没有活跃状态的咚，或玩家选择不发动（赋予目标处选 0 张），则整个效果跳过、不支付代价。
///   - 玩家先选赋予目标（领袖或角色，min=0 可放弃）；选定后支付代价：
///     赋予 1 张活跃咚 + 自身去废弃，然后让玩家从对方角色中选最多 1 张本回合力量 -3000。
/// </summary>
public class OP13_007_AceSaboLuffy : IScriptedEffect
{
    public string CardNumber => "OP13-007";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];

        // 代价前置检查：必须存在活跃状态的咚!! 才能赋予
        int activeDon = me.CostArea.Count(d => d.State == DonState.Active);
        if (activeDon <= 0) return;

        // 选择赋予目标：我方领袖或 1 张角色（整体可选 → min=0，可放弃发动）
        var attachCandidates = new List<CardInstance> { me.Leader };
        attachCandidates.AddRange(me.Characters);

        var chosenAttach = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "赋予我方领袖或 1 张角色 1 张活跃状态的咚!!（并将此角色放置废弃区；不选则放弃发动）",
            attachCandidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosenAttach.Count == 0) return;

        var attachTarget = attachCandidates.First(c => c.Id.ToString() == chosenAttach[0]);

        // 支付代价 1：赋予 1 张活跃状态的咚!!
        AtomicOps.AttachDonFromCost(me, attachTarget.Id, 1, DonState.Active);

        // 支付代价 2：将此角色（自身）放置到废弃区（自送废弃，不触发"被 KO 时"效果）
        AtomicOps.KO(s, ctx.OwnerIndex, ctx.Source);

        // 效果：本回合中，对方最多 1 张角色力量 -3000
        if (opp.Characters.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选最多 1 张对方角色，本回合力量 -3000",
            opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = opp.Characters.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddPowerThisTurn(target, -3000);
    }
}
