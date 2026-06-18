using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-035 约撒（角色，风）
/// 【我方的回合中】当此角色转为休息状态时，对方最多 1 张处于休息状态且费用不高于 4 的角色，
///                 在下个对方的重置阶段中不会转为活跃状态。
///
/// 实现说明：
///   - 用 OnCharRested watcher（仅当被横置的是本卡自身、且为我方回合时触发）。
///   - 候选 = 对方处于休息状态(IsTapped) 且原本费用≤4 的角色；
///     "下个重置阶段不活跃"用 AtomicOps.PreventActivateNextReset。
/// </summary>
public class OP14_035_Yosaku : IScriptedEffect
{
    public string CardNumber => "OP14-035";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharRested;

    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        if (ctx.State.CurrentTurnPlayer != owner) return;

        var restedId = ctx.Vars.TryGetValue("restedCardId", out var v) ? v as string : null;
        if (restedId != ctx.Source.Id.ToString()) return; // 仅本卡被横置

        var opp = ctx.State.Players[1 - owner];
        var cands = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 4).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(owner, "OpponentRestingCharacter",
            "选择对方最多 1 张休息状态且费用≤4 的角色，使其在下个对方重置阶段不会转为活跃",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PreventActivateNextReset(tgt);
        }
    }
}
