using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-094 剃（事件 / CP9）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +2000。之后，我方废弃区中有 10 张或更多
///   卡牌的场合，将我方最多 1 张拥有的特征中包含 &lt;CP&gt; 的角色放回其持有者的手牌。
/// 【触发】将我方最多 1 张角色放回其持有者的手牌。
///
/// 说明：
///   - 反击：先选最多 1 张我方领袖或角色本战斗 +2000；之后若废弃区 ≥10 张，可将 1 张含
///     &lt;CP&gt; 特征的我方角色退回手牌（最多 1 张，可选）。
///   - 触发：将我方最多 1 张角色退回手牌（可选 min=0）。
///   - "含&lt;CP&gt;特征"用关键词包含 "CP" 子串判定。
/// </summary>
public class OP07_094_Soru : IScriptedEffect
{
    public string CardNumber => "OP07-094";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventCounter || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventCounter)
            await ResolveCounter(ctx);
        else if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
            await ResolveTrigger(ctx);
    }

    // 【反击】最多 1 张领袖/角色 +2000；之后若废弃区≥10，退回 1 张含<CP>角色
    private static async Task ResolveCounter(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

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

        // 之后：废弃区 ≥10 张时，将我方最多 1 张含<CP>角色退回手牌
        if (me.Trash.Count >= 10)
        {
            var cpChars = me.Characters.Where(c =>
                c.Info.Keywords.Any(k => k.Contains("CP"))).ToList();
            if (cpChars.Count > 0)
            {
                var ch2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                    "将我方最多 1 张含<CP>特征的角色放回手牌",
                    cpChars.Select(c => c.Id.ToString()).ToList(), 0, 1);
                if (ch2.Count > 0)
                {
                    var tgt = cpChars.First(c => c.Id.ToString() == ch2[0]);
                    AtomicOps.BounceToHand(s, ctx.OwnerIndex, tgt);
                }
            }
        }
    }

    // 【触发】将我方最多 1 张角色放回手牌
    private static async Task ResolveTrigger(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        if (me.Characters.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "将我方最多 1 张角色放回手牌",
            me.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = me.Characters.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.BounceToHand(s, ctx.OwnerIndex, tgt);
        }
    }
}
