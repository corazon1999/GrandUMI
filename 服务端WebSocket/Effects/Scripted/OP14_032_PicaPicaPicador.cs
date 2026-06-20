using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-032 人类金刚钻（角色，风）
/// 【我方的回合中】当此角色转为休息状态时，将对方最多 1 张费用不高于 4 的角色转为休息状态。
///
/// 实现说明：
///   - 用 OnCharRested watcher（仅当被横置的是本卡自身、且为我方回合时触发）。
///   - 候选 = 对方原本费用≤4 且当前活跃的角色；转休息用 AtomicOps.RestCard。
/// </summary>
public class OP14_032_PicaPicaPicador : IScriptedEffect
{
    public string CardNumber => "OP14-032";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharRested;

    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        if (ctx.State.CurrentTurnPlayer != owner) return;

        var restedId = ctx.Vars.TryGetValue("restedCardId", out var v) ? v as string : null;
        if (restedId != ctx.Source.Id.ToString()) return; // 仅本卡被横置

        var opp = ctx.State.Players[1 - owner];
        var cands = opp.Characters.Where(c => c.Info.Cost <= 4 && !c.IsTapped).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(owner, "OpponentCharacter",
            "将对方最多 1 张费用≤4 的角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
