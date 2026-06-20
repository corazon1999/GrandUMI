using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-018 橡皮橡皮大猿王枪乱打（事件，炎 2 费，FILM/草帽一伙）
/// 【主要】本回合中，我方最多 1 张领袖或角色力量 +3000。之后，对方场上存在力量为 7000 或更高的
///   角色的场合，本回合中，我方最多 1 张领袖或角色力量 +1000。
/// 【触发】将对方最多 1 张力量不高于 5000 的角色 KO。
///
/// 实现说明：
///   - 主要含"之后"连锁与"对方场上存在力量≥7000角色"的条件判断，DSL 无对应条件键，故脚本。
///   - 两段加成各自独立选择目标（均为"最多 1 张"）。第二段需先满足对方有力量≥7000角色。
///   - 当前力量评估用 CurrentPowerOf（含持续效果与贴咚）。
/// </summary>
public class OP06_018_GomuGomuNoOzaruOGun : IScriptedEffect
{
    public string CardNumber => "OP06-018";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】将对方最多 1 张力量≤5000 的角色 KO
            var cands = opp.Characters.Where(c => ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, c) <= 5000).ToList();
            if (cands.Count == 0) return;
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张力量≤5000 的角色 KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
            return;
        }

        // ── 【主要】第一段：我方最多 1 张领袖或角色 +3000 ──
        var targets1 = new List<CardInstance> { me.Leader };
        targets1.AddRange(me.Characters);
        var ch1 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本回合中，我方最多 1 张领袖或角色力量 +3000",
            targets1.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (ch1.Count > 0)
        {
            var t1 = targets1.First(c => c.Id.ToString() == ch1[0]);
            AtomicOps.AddPowerThisTurn(t1, 3000);
        }

        // ── 第二段：对方场上存在力量≥7000 的角色时，再 +1000 ──
        bool oppHasBig = opp.Characters.Any(c => ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, c) >= 7000);
        if (!oppHasBig) return;

        var targets2 = new List<CardInstance> { me.Leader };
        targets2.AddRange(me.Characters);
        var ch2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本回合中，我方最多 1 张领袖或角色力量 +1000",
            targets2.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (ch2.Count > 0)
        {
            var t2 = targets2.First(c => c.Id.ToString() == ch2[0]);
            AtomicOps.AddPowerThisTurn(t2, 1000);
        }
    }
}
