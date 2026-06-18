using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-041 丧魂弗兰奇风速计BOXING剑（事件 / 时光旅诗·草帽一伙）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +2000。
///   之后，我方领袖拥有《时光旅诗》特征，且我方场上存在 2 张或更多处于休息状态的角色的场合，
///   将我方最多 2 张角色转为活跃状态。
/// 【触发】将对方最多 1 张费用不高于 4 的角色转为休息状态。
///
/// 实现说明 / 简化点：
///   - 反击：先选最多 1 张我方领袖/角色本战斗 +2000；之后按条件（领袖《时光旅诗》且我方休息角色≥2）
///     再选最多 2 张我方角色转为活跃。条件判定在 +2000 之后进行。
///   - 触发：将对方最多 1 张费用≤4 的角色转为休息状态（强制，min=0 允许跳过）。
/// </summary>
public class OP09_041_FrankyRadarBoxingSword : IScriptedEffect
{
    public string CardNumber => "OP09-041";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventCounter)
            await ResolveCounter(ctx);
        else if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
            await ResolveTrigger(ctx);
    }

    private static async Task ResolveCounter(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 本次战斗中，我方最多 1 张领袖或角色 +2000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张领袖或角色，本次战斗力量 +2000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 2000);
        }

        // 之后：领袖《时光旅诗》且我方休息角色≥2 → 最多 2 张我方角色转为活跃
        bool cond = me.Leader.Info.HasKeyword("时光旅诗")
                    && me.Characters.Count(c => c.IsTapped) >= 2;
        if (!cond) return;

        var restedChars = me.Characters.Where(c => c.IsTapped).ToList();
        if (restedChars.Count == 0) return;
        var actChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择最多 2 张我方角色转为活跃状态",
            restedChars.Select(c => c.Id.ToString()).ToList(), 0, 2);
        foreach (var cid in actChosen)
        {
            var c = restedChars.FirstOrDefault(x => x.Id.ToString() == cid);
            if (c is not null) AtomicOps.ActivateCard(c);
        }
    }

    private static async Task ResolveTrigger(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var cands = opp.Characters.Where(c => c.Info.Cost <= 4 && !c.IsTapped).ToList();
        if (cands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤4 的角色转为休息状态",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(tgt);
        }
    }
}
