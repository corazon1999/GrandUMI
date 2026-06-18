using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-102 爱迪生（角色 / 光 / 3 费 / 2000 / 艾格赫德・科学家）
/// 【触发】抽取 1 张卡牌，将对方最多 1 张费用不高于 3 的角色转为休息状态。
/// 【启动主要】可以将此角色放置到废弃区：我方生命卡牌的张数不多于对方生命卡牌的张数的场合，
///   抽取 1 张卡牌。之后，将对方最多 1 张费用不高于 3 的角色转为休息状态。
///
/// 说明：
///   - 两个触发都需要“将对方最多 1 张费用≤3 的角色转为休息状态”，现成 DSL 候选种类无
///     “费用≤3 的对方角色”，故整张卡用脚本实现。
///   - 【启动主要】成本：将此角色放置到废弃区（同步 KOCard，不触发“被 KO 时”效果）。
///     该成本无条件支付（“可以”=可选，先 ConfirmOptional 询问）。生命条件不满足时仍已离场，
///     但抽牌/转休息不发动（忠实原文：成本与是否满足后续条件无关）。
///   - “最多 1 张”转休息为可选（min=0）。
/// </summary>
public class OP13_102_Edison : IScriptedEffect
{
    public string CardNumber => "OP13-102";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnLifeRevealTrigger || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var opp = s.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】抽 1，并将对方最多 1 张费用≤3 的角色转休息
            AtomicOps.Draw(s, ctx.OwnerIndex, 1);
            await RestOpponentCostLe3(ctx, opp);
            return;
        }

        // 【启动主要】
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "爱迪生【启动主要】：将此角色放置废弃区，若我方生命≤对方生命则抽 1 并将对方 1 张费用≤3 角色转休息？");
        if (!use) return;

        // 成本：将自身放置到废弃区（同步，不触发被 KO 时效果）
        AtomicOps.KO(s, ctx.OwnerIndex, ctx.Source);

        // 条件：我方生命牌数 ≤ 对方生命牌数
        if (me.LifeCount > opp.LifeCount) return;

        AtomicOps.Draw(s, ctx.OwnerIndex, 1);
        await RestOpponentCostLe3(ctx, opp);
    }

    private static async Task RestOpponentCostLe3(EffectContext ctx, PlayerState opp)
    {
        var candidates = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        if (candidates.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterCostLe3",
            "将对方最多 1 张费用≤3 的角色转为休息状态",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;
        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.RestCard(target);
    }
}
