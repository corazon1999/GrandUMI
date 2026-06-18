using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-116 恶魔风脚 颊肉Shoot（事件 / 光 / 温思默克家）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +6000。
///   之后，双方生命卡牌合计张数不多于 4 张的场合，将对方最多 1 张费用不高于 2 的角色 KO。
/// 【触发】抽取 1 张卡牌。（触发节由引擎/DSL 处理，此处仅实现反击。）
///
/// 实现说明：
///   - 反击(EventCounter)：先选我方 1 张领袖/角色本次战斗 +6000；
///     之后双方生命合计≤4 时，选 KO 对方 1 张费用≤2 角色。
/// </summary>
public class OP04_116_DiableJambeHohnikuShoot : IScriptedEffect
{
    public string CardNumber => "OP04-116";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】抽取 1 张卡牌
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        // 【反击】我方最多 1 张领袖或角色 +6000（本次战斗）
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "本次战斗中，我方最多 1 张领袖或角色力量 +6000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 6000);
        }

        // 之后：双方生命合计≤4 时，KO 对方最多 1 张费用≤2 角色
        if (me.LifeCount + opp.LifeCount <= 4)
        {
            var cands = opp.Characters
                .Where(c => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, c) <= 2)
                .ToList();
            if (cands.Count > 0)
            {
                var extra = new Dictionary<string, object?>
                {
                    ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
                };
                var koPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                    "将对方最多 1 张费用≤2 的角色 KO",
                    cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
                if (koPick.Count > 0)
                {
                    var tgt = cands.First(c => c.Id.ToString() == koPick[0]);
                    AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
                }
            }
        }
    }
}
