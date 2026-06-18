using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-029 达斯琪（角色）
/// 【对方的回合中】此角色因对方的效果将要离开场上的场合，可以改为将我方的 1 张卡牌转为休息状态，
///   使此角色不离场。
/// 【启动主要】【每回合1次】可以将我方 2 张卡牌转为休息状态：
///   直到下个对方的结束阶段结束时为止，此角色的力量 +2000。
///
/// 实现说明 / 简化点：
///   - "将要离开场上 → 改为不离场" 通过 PreKO 钩子实现（引擎的离场替换通道即 PreKO；
///     最常见的离场为被 KO，简化为只处理 KO，且不严格区分是否"对方的效果"，仅在对方回合中生效）。
///     成本：将我方 1 张活跃卡牌(领袖/角色)转为休息状态；支付后调用 MarkPreventKO 取消本次 KO。
///   - 【启动主要】成本为将我方 2 张活跃卡牌(领袖/角色)转为休息状态；收益为自身力量 +2000。
///     "直到下个对方结束阶段" 的力量加成用 AddPowerThisTurn 近似（启动主要在我方回合使用，
///     +2000 覆盖本回合；引擎无"直到下个对方结束阶段"的专用力量通道，按惯例近似为本回合）。
///   - 每回合 1 次用 TurnOnceUsed 控制。
/// </summary>
public class OP14_029_Tashigi : IScriptedEffect
{
    public string CardNumber => "OP14-029";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.PreKO || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.PreKO)
        {
            // 仅在对方的回合中可用
            if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;

            // 候选：我方活跃(未横置)的领袖或角色
            var rests = new List<CardInstance>();
            if (!me.Leader.IsTapped) rests.Add(me.Leader);
            rests.AddRange(me.Characters.Where(c => !c.IsTapped));
            if (rests.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "达斯琪：是否将我方 1 张卡牌转为休息状态，使此角色不离场？");
            if (!use) return;

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "将我方 1 张卡牌转为休息状态（成本）",
                rests.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (chosen.Count == 0) return;

            var tgt = rests.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
            ctx.State.MarkPreventKO(self.Id);
            return;
        }

        // ── ActivatedMain：每回合 1 次 ──
        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        var costCands = new List<CardInstance>();
        if (!me.Leader.IsTapped) costCands.Add(me.Leader);
        costCands.AddRange(me.Characters.Where(c => !c.IsTapped));
        if (costCands.Count < 2) return; // 不足 2 张活跃卡牌无法支付成本

        bool act = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "达斯琪【启动主要】：将我方 2 张卡牌转为休息状态，使此角色力量 +2000？");
        if (!act) return;

        var picked = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "将我方 2 张卡牌转为休息状态（成本）",
            costCands.Select(c => c.Id.ToString()).ToList(), 2, 2);
        if (picked.Count < 2) return;

        foreach (var cid in picked)
        {
            var c = costCands.First(x => x.Id.ToString() == cid);
            AtomicOps.RestCard(c);
        }

        AtomicOps.AddPowerThisTurn(self, 2000);
        me.TurnOnceUsed.Add(key);
    }
}
