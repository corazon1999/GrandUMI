using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-095 铁块（事件 / CP9）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +4000。之后，我方废弃区中有 10 张或更多
///   卡牌的场合，本次战斗中，该卡牌的力量 +2000。
/// 【触发】本回合中，我方最多 1 张领袖或角色力量 +1000。
///
/// 说明：
///   - 反击：选最多 1 张我方领袖/角色本战斗 +4000；之后若废弃区 ≥10 张，则同一张卡再 +2000。
///   - 触发：选最多 1 张我方领袖/角色本回合 +1000。
/// </summary>
public class OP07_095_Tekkai : IScriptedEffect
{
    public string CardNumber => "OP07-095";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventCounter)
            await ResolveCounter(ctx);
        else if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
            await ResolveTrigger(ctx);
    }

    // 【反击】最多 1 张领袖/角色 +4000；废弃区≥10 时同一张再 +2000
    private static async Task ResolveCounter(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张领袖或角色，本次战斗力量 +4000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddPowerThisBattle(tgt, 4000);

        // 之后：废弃区 ≥10 张时，该卡牌本战斗再 +2000
        if (me.Trash.Count >= 10)
            AtomicOps.AddPowerThisBattle(tgt, 2000);
    }

    // 【触发】最多 1 张领袖/角色本回合 +1000
    private static async Task ResolveTrigger(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张领袖或角色，本回合力量 +1000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, 1000);
        }
    }
}
