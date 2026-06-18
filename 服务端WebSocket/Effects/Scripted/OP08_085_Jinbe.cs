using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-085 甚平（角色 / 地 / 5 费 / 6000 / 鱼人族·草帽一伙）
/// 【咚!!×1】【攻击时】我方场上存在费用为 8 或更高的角色的场合，
///   将对方最多 1 张费用不高于 4 的角色 KO。
///
/// 说明 / 简化点：
///   - 【咚!!×1】发动门槛：此角色被赋予的咚!! ≥1（AttachedDonCount(self.Id)）。
///   - 条件"我方场上存在费用≥8的角色"用 me.Characters 按 CurrentCost() 判定（脚本可直接遍历）。
/// </summary>
public class OP08_085_Jinbe : IScriptedEffect
{
    public string CardNumber => "OP08-085";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】门槛
        if (me.AttachedDonCount(self.Id) < 1) return;

        // 条件：我方场上存在费用≥8的角色
        if (!me.Characters.Any(c => ctx.State.CurrentCostOf(c) >= 8)) return;

        // 效果：将对方最多 1 张费用≤4 的角色 KO
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 4).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤4 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
