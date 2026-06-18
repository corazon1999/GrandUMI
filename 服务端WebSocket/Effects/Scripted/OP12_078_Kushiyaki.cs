using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-078 串烧（事件）
/// 【主要】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，抽取 1 张卡牌。
///   之后，本回合中，对方最多 1 张角色力量 -3000。
///
/// 实现说明：
/// - "抽取 1 张"是受咚数条件约束的；"-3000"部分为无条件，故两段分别处理
///   （DSL 的 main.if 会同时门控整个块，无法表达此差异，因此用脚本）。
/// - 咚数比较与 DSL 的 selfDonNotMoreThanOpp 保持一致：
///   me.TotalDonInCostArea &lt;= opp.TotalDonInCostArea。
/// - "对方最多 1 张角色力量 -3000"为可选（min=0），无对象时跳过。
/// </summary>
public class OP12_078_Kushiyaki : IScriptedEffect
{
    public string CardNumber => "OP12-078";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 我方咚数不多于对方咚数 → 抽 1 张
        if (me.TotalDonInCostArea <= opp.TotalDonInCostArea)
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

        // 之后：本回合中，对方最多 1 张角色力量 -3000
        var candidates = opp.Characters.ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "本回合中，对方最多 1 张角色力量 -3000",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is null) return;

        AtomicOps.AddPowerThisTurn(target, -3000);
    }
}
