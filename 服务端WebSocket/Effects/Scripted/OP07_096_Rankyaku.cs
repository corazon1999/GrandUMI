using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-096 暴风脚（事件 / CP9）
/// 【主要】抽取 1 张卡牌。之后，我方废弃区中有 10 张或更多卡牌的场合，本回合中，对方最多 1 张角色费用 -3。
/// 【触发】将对方最多 1 张费用不高于 3 的角色 KO。
///
/// 说明：
///   - 主要：无条件抽 1；之后若废弃区 ≥10 张，可选对方最多 1 张角色本回合费用 -3。
///   - 触发：KO 对方最多 1 张费用≤3 的角色。
/// </summary>
public class OP07_096_Rankyaku : IScriptedEffect
{
    public string CardNumber => "OP07-096";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventMain)
            await ResolveMain(ctx);
        else if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
            await ResolveTrigger(ctx);
    }

    // 【主要】抽 1；之后废弃区≥10 时对方角色费用 -3
    private static async Task ResolveMain(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];

        AtomicOps.Draw(s, ctx.OwnerIndex, 1);

        if (me.Trash.Count >= 10 && opp.Characters.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多 1 张角色，本回合费用 -3",
                opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = opp.Characters.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddCostModifier(tgt, -3, KeywordDuration.ThisTurn);
            }
        }
    }

    // 【触发】KO 对方最多 1 张费用≤3 的角色
    private static async Task ResolveTrigger(EffectContext ctx)
    {
        var s = ctx.State;
        var opp = s.Players[1 - ctx.OwnerIndex];

        var koCands = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        if (koCands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤3 的角色 KO",
            koCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = koCands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(s, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
