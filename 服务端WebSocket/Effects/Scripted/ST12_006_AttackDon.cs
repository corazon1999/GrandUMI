using System.Linq;
using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST12-006（角色）
/// 【咚!!×1】【攻击时】选择以下的1项。
///   ・将对方最多1张费用不高于2的角色转为休息状态。
///   ・将对方最多1张处于休息状态且费用不高于2的角色KO。
///
/// 实现:缺条目(攻击时效果原先完全未实现,反馈#127审计发现)。【咚×1】门槛脚本自检+二选一。
/// </summary>
public class ST12_006_AttackDon : IScriptedEffect
{
    public string CardNumber => "ST12-006";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        // 【咚!!×1】
        if (me.AttachedDonCount(ctx.Source.Id) < 1) return;

        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "ST12-006【攻击时】选择以下1项",
            new List<string> { "将对方最多1张费用≤2的角色转为休息", "将对方最多1张休息且费用≤2的角色KO" });

        if (opt == 0)
        {
            var cands = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
            if (cands.Count == 0) return;
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多1张费用≤2的角色转为休息", cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (pick.Count > 0)
                AtomicOps.RestCard(cands.First(c => c.Id.ToString() == pick[0]));
        }
        else
        {
            var cands = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 2).ToList();
            if (cands.Count == 0) return;
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多1张休息且费用≤2的角色KO", cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (pick.Count > 0)
                await AtomicOps.KOByEffectAsync(ctx.State, oppIdx, cands.First(c => c.Id.ToString() == pick[0]), ctx.Prompts, ctx.OwnerIndex);
        }
    }
}
