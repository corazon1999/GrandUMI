using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-010 老鼠（角色）
/// 【启动主要】【每回合1次】赋予 1 张领袖或角色最多 1 张其持有者的休息状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 可选择我方或对方的领袖/角色作为目标；咚!! 从"该卡持有者"的费用区取出附着，
///     与"其持有者的休息状态的咚!!"语义一致（引擎将取出的咚置为 Attached）。
///   - 取费用区中活跃咚附着（受该持有者费用区剩余咚限制）。
/// </summary>
public class OP15_010_Nezumi : IScriptedEffect
{
    public string CardNumber => "OP15-010";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 候选：双方领袖与角色
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        targets.Add(opp.Leader);
        targets.AddRange(opp.Characters);

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = targets.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "LeaderOrCharacter",
            "选择 1 张领袖或角色，赋予其持有者 1 张休息状态的咚!!",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var target = targets.First(c => c.Id.ToString() == chosen[0]);
        // 判定目标持有者：从其持有者的费用区取咚
        var ownerState = me.Characters.Contains(target) || me.Leader.Id == target.Id ? me : opp;
        AtomicOps.AttachDonFromCost(ownerState, target.Id, 1, DonState.Active);

        me.TurnOnceUsed.Add(key);
    }
}
