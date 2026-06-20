using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-115 你不是我喜欢的类型……（事件，光）
/// 【反击】我方领袖为"白星"的场合，本次战斗中，我方最多 1 张领袖或角色力量 +4000。
/// 【触发】将对方最多 1 张费用不高于 2 的角色 KO。
///
/// 实现说明：
///   - 【反击】的收益带有"我方领袖为白星"的条件，DSL 的 counter 节无法表达 leaderNameEquals，
///     故用脚本：领袖名含"白星"时才提供选择 +4000。
///   - 【触发】将对方最多 1 张费用≤2 的角色 KO。
/// </summary>
public class OP11_115_NotMyType : IScriptedEffect
{
    public string CardNumber => "OP11-115";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // ── 【反击】 ──
        if (ctx.Trigger == EffectTrigger.EventCounter)
        {
            // 仅当我方领袖为"白星"时本效果有收益
            if (!me.Leader.Info.NameContains("白星")) return;

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
            return;
        }

        // ── 【触发】将对方最多 1 张费用≤2 的角色 KO ──
        var cands = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
        if (cands.Count == 0) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤2 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var target = cands.FirstOrDefault(c => c.Id.ToString() == pick[0]);
            if (target is not null)
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, target);
        }
    }
}
