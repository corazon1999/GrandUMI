using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-031 水牛（角色）
/// 【攻击时】【每回合1次】我方场上存在2张或更多处于休息状态的角色的场合，
///   将我方最多1张处于休息状态且费用为1的角色转为活跃状态。
///
/// 实现说明：
///   - "处于休息状态"用 CardInstance.IsTapped 判定；"费用为1"按原本费用 Info.Cost==1。
/// </summary>
public class OP05_031_Buffalo : IScriptedEffect
{
    public string CardNumber => "OP05-031";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合1次
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 条件：我方场上≥2张休息状态角色
        int restingCount = me.Characters.Count(c => c.IsTapped);
        if (restingCount < 2) return;

        // 候选：休息状态且费用为1的角色
        var candidates = me.Characters.Where(c => c.IsTapped && c.Info.Cost == 1).ToList();
        if (candidates.Count == 0) return;

        me.TurnOnceUsed.Add(key);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方最多1张处于休息状态且费用为1的角色转为活跃状态",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.ActivateCard(target);
    }
}
