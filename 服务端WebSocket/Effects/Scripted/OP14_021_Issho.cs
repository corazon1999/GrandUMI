using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-021 一生（角色）
/// 【我方的回合中】当此角色因效果转为休息状态时，可以将我方生命区最上方的 1 张卡牌加入手牌。
///   那样做的场合，对方最多 1 张处于休息状态的角色或舞台，在下个对方的重置阶段中不会转为活跃状态。
///
/// 实现说明 / 简化点：
///   - 通过反应式 watcher OnCharRested 触发，仅当被横置的是本卡自身、且为我方回合时发动。
///   - "可以"为可选效果，用 ConfirmOptional 询问。
///   - "对方休息角色或舞台不活跃"用 CannotActivateNextReset 标记（PreventActivateNextReset）。
///     舞台同为 CardInstance 且也带该标记字段，故角色与舞台均纳入候选。
/// </summary>
public class OP14_021_Issho : IScriptedEffect
{
    public string CardNumber => "OP14-021";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharRested;

    public async Task Resolve(EffectContext ctx)
    {
        // 仅我方回合
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        // 仅本卡自身被横置时触发
        var restedId = ctx.Vars.TryGetValue("restedCardId", out var v) ? v as string : null;
        if (restedId != ctx.Source.Id.ToString()) return;

        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "一生：将生命区最上方 1 张卡牌加入手牌？（那样做可使对方 1 张休息状态的角色/舞台下个重置阶段不活跃）");
        if (!use) return;

        // 将生命区最上方 1 张加入手牌
        var top = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        me.Hand.Add(top);

        // 之后：对方最多 1 张休息状态的角色或舞台，下个对方重置阶段不会转为活跃
        var cands = new List<CardInstance>();
        cands.AddRange(opp.Characters.Where(c => c.IsTapped));
        if (opp.StageCard is { IsTapped: true } stage) cands.Add(stage);
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterOrStage",
            "选择对方最多 1 张休息状态的角色或舞台，使其在下个对方重置阶段不会转为活跃",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.PreventActivateNextReset(tgt);
        }
    }
}
