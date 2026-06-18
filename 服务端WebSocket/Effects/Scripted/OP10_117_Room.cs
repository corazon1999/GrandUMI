using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-117 ROOM（事件，光）
/// 【反击】我方生命卡牌不多于 1 张的场合，本次战斗中，我方最多 1 张领袖或角色力量 +3000。
///   之后，将我方最多 1 张费用不高于 5 的角色转为活跃状态。
/// 【触发】抽取 1 张卡牌。
///
/// 实现说明：
///   - 【反击】前置门槛 = 我方生命 ≤ 1。满足后：最多选 1 张领袖/角色 +3000（本次战斗），
///     再将我方最多 1 张费用≤5 的角色转为活跃状态（ActivateCard）。
///   - 【触发】抽 1。
/// </summary>
public class OP10_117_Room : IScriptedEffect
{
    public string CardNumber => "OP10-117";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // ── 【触发】抽 1 ──
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        // ── 【反击】 ──
        // 前置门槛：我方生命卡牌不多于 1 张
        if (me.LifeCount > 1) return;

        // 第一步：我方最多 1 张领袖或角色 +3000（本次战斗）
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，我方最多 1 张领袖或角色力量 +3000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var target = targets.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
            if (target is not null)
                AtomicOps.AddPowerThisBattle(target, 3000);
        }

        // 第二步：将我方最多 1 张费用≤5 的角色转为活跃状态
        var actCands = me.Characters.Where(c => c.Info.Cost <= 5 && c.IsTapped).ToList();
        if (actCands.Count == 0) return;
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方最多 1 张费用≤5 的角色转为活跃状态",
            actCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var target = actCands.FirstOrDefault(c => c.Id.ToString() == pick[0]);
            if (target is not null)
                AtomicOps.ActivateCard(target);
        }
    }
}
