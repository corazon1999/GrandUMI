using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-115 我们在新世界再见吧!!（事件，光）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +4000。
///   之后，我方生命卡牌为 0 张的场合，抽取 1 张卡牌。
/// 【触发】将对方最多 1 张费用不高于对方生命卡牌张数的角色 KO。
///
/// 实现说明：
///   - 【反击】先让玩家最多选 1 张领袖/角色 +4000（本次战斗）；随后若我方生命为 0 则抽 1。
///   - 【触发】动态阈值：候选 = 对方费用 ≤ 对方生命张数 的角色，最多 KO 1 张。
/// </summary>
public class OP10_115_SeeYouInTheNewWorld : IScriptedEffect
{
    public string CardNumber => "OP10-115";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ── 【反击】 ──
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            var targets = new List<CardInstance> { me.Leader };
            targets.AddRange(me.Characters);

            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "本次战斗中，我方最多 1 张领袖或角色力量 +4000",
                targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var target = targets.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
                if (target is not null)
                    AtomicOps.AddPowerThisBattle(target, 4000);
            }

            // 之后：我方生命为 0 张时抽 1
            if (me.LifeCount == 0)
                AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        // ── 【触发】 ──
        // 候选：对方费用不高于对方生命卡牌张数的角色
        int threshold = opp.LifeCount;
        var cands = opp.Characters.Where(c => c.Info.Cost <= threshold).ToList();
        if (cands.Count == 0) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            $"将对方最多 1 张费用≤{threshold}（对方生命张数）的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var target = cands.FirstOrDefault(c => c.Id.ToString() == pick[0]);
            if (target is not null)
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, target);
        }
    }
}
