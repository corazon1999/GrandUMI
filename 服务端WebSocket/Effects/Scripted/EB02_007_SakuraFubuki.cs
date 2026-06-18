using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-007 刻蹄 樱吹雪（事件 / 炎）
/// 【主要】本回合中，我方的领袖和角色合计最多 3 张力量 +1000。
///   之后，将对方最多 1 张力量不高于 3000 的角色 KO。
/// 【触发】将对方最多 1 张力量不高于 4000 的角色 KO。
///
/// 实现说明：
///   - 多目标 +1000 用逐张 ChooseCards(0..3) 后对每张 AddPowerThisTurn。
///   - KO 段按当前力量筛选候选(CurrentPowerOf ≤ 3000 / 4000)，玩家选 0..1 张 KO。
/// </summary>
public class EB02_007_SakuraFubuki : IScriptedEffect
{
    public string CardNumber => "EB02-007";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            // 本回合中，我方领袖和角色合计最多 3 张 +1000
            var boostCands = new List<CardInstance> { me.Leader };
            boostCands.AddRange(me.Characters);
            if (boostCands.Count > 0)
            {
                var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                    "选择最多 3 张我方领袖/角色本回合力量 +1000",
                    boostCands.Select(c => c.Id.ToString()).ToList(), 0, 3);
                foreach (var id in ch)
                {
                    var c = boostCands.First(x => x.Id.ToString() == id);
                    AtomicOps.AddPowerThisTurn(c, 1000);
                }
            }

            // 之后：将对方最多 1 张力量≤3000 的角色 KO
            await KoOpponent(ctx, opp, 3000);
            return;
        }

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】将对方最多 1 张力量≤4000 的角色 KO
            await KoOpponent(ctx, opp, 4000);
            return;
        }
    }

    private static async Task KoOpponent(EffectContext ctx, PlayerState opp, int maxPower)
    {
        var cands = opp.Characters
            .Where(c => ctx.State.CurrentPowerOf(1 - ctx.OwnerIndex, c) <= maxPower)
            .ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            $"将对方最多 1 张力量≤{maxPower}的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
