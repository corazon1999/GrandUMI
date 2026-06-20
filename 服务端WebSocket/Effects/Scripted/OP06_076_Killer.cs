using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-076 砍人镰造（角色）
/// 【我方的回合中】【每回合1次】当我方场上的咚!!放回咚!!卡组时，将对方最多1张费用不高于2的角色KO。
///
/// 实现说明 / 简化点：
/// - 用 Wave2 反应式 watcher OnDonReturnedToDeck（文本含"放回咚!!卡组时"）监听咚放回事件。
/// - 仅在我方回合生效；【每回合1次】用 TurnOnceUsed 去重。
/// - "最多1张"为可选，玩家可不选；候选为对方费用≤2 的角色（当前费用）。
/// </summary>
public class OP06_076_Killer : IScriptedEffect
{
    public string CardNumber => "OP06-076";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDonReturnedToDeck;

    public async Task Resolve(EffectContext ctx)
    {
        // 仅【我方的回合中】生效
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【每回合1次】
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 2).ToList();
        if (cands.Count == 0) return;

        me.TurnOnceUsed.Add(key);

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多1张费用不高于2的角色KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
