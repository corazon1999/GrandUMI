using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-080 卡古（角色 / 地 / CP9）
/// 【登场时】可以将我方废弃区中 2 张拥有的特征中包含&lt;CP&gt;的卡牌自选顺序放回卡组最下方：
///   将对方最多 1 张费用不高于 3 的角色KO。
///
/// 实现说明：
///   - "可以…"=可选；成本为将废弃区 2 张含 CP 特征卡放回卡组底（自选顺序简化为所选顺序放底）。
///   - &lt;CP&gt; 子串匹配：任一特征字符串包含 "CP"（覆盖 CP9/CP0/CP-0 等）。
///   - 成本与 KO 结果强耦合：须废弃区有 2 张候选并完成放回后才结算 KO。
///   - KO 用 CurrentCostOf 评估对方角色当前费用 ≤3。
/// </summary>
public class OP03_080_Kaku : IScriptedEffect
{
    public string CardNumber => "OP03-080";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        bool HasCp(CardInstance c) => c.Info.Keywords.Any(k => k.Contains("CP"));

        var cpTrash = me.Trash.Where(HasCp).ToList();
        if (cpTrash.Count < 2) return; // 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卡古【登场时】：将废弃区 2 张含<CP>特征卡放回卡组最下方，KO 对方最多 1 张费用≤3 的角色？");
        if (!use) return;

        // 成本：选 2 张含 CP 特征的废弃区卡放回卡组底（按所选顺序）
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cpTrash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCpToDeckBottom",
            "选择 2 张含<CP>特征的废弃区卡放回卡组最下方",
            cpTrash.Select(c => c.Id.ToString()).ToList(), 2, 2, extra);
        if (chosen.Count < 2) return; // 成本未完成

        foreach (var id in chosen)
        {
            var card = cpTrash.First(c => c.Id.ToString() == id);
            AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        // 收益：KO 对方最多 1 张费用≤3 的角色
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(oppIdx, c) <= 3).ToList();
        if (cands.Count == 0) return;
        var koExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var koPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "KO 对方最多 1 张费用≤3 的角色",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, koExtra);
        if (koPick.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == koPick[0]);
            AtomicOps.KO(ctx.State, oppIdx, tgt);
        }
    }
}
