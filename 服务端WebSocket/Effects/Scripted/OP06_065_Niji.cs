using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-065 温思默克·尼智（费用5 角色）
/// 【登场时】我方场上咚!!的张数不多于对方场上咚!!的张数的场合，选择以下的 1 项。
/// ・将对方最多 1 张费用不高于 2 的角色KO。
/// ・将对方最多 1 张费用不高于 4 的角色放回其持有者的手牌。
///
/// 实现说明：
/// - "我方咚数不多于对方咚数" = 双方费用区咚总数比较（含活跃/休息/赋予中）。
/// - 二选一用 ChooseOption；目标选择各自范围（费用≤2 KO / 费用≤4 退回），均可"最多 1 张"。
/// </summary>
public class OP06_065_Niji : IScriptedEffect
{
    public string CardNumber => "OP06-065";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方咚数 ≤ 对方咚数
        if (me.CostArea.Count > opp.CostArea.Count) return;

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "尼智【登场时】选择 1 项",
            new[] { "将对方最多1张费用≤2的角色KO", "将对方最多1张费用≤4的角色放回手牌" });

        if (opt == 0)
        {
            var cands = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张费用≤2 的对方角色KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }
        else
        {
            var cands = opp.Characters.Where(c => c.Info.Cost <= 4).ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张费用≤4 的对方角色放回手牌",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.BounceToHand(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }
    }
}
