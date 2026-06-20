using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-030 佩德罗（角色）
/// 【阻挡者】（关键词，由引擎处理，此脚本不实现）
/// 【KO时】选择以下的1项：
///   ・将对方最多1张咚!!转为休息状态。
///   ・将对方最多1张处于休息状态且费用不高于6的角色KO。
///
/// 实现说明 / 简化点：
///   - 二选一用 ChooseOption；"最多1张"在各分支内用 ChooseCards(min=0,max=1)。
///   - "将咚!!转为休息"通过把对方费用区中1张活跃咚设为休息状态实现。
/// </summary>
public class OP08_030_Pedro : IScriptedEffect
{
    public string CardNumber => "OP08-030";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "佩德罗【KO时】选择1项",
            new[] { "将对方最多1张咚!!转为休息状态", "KO对方最多1张休息状态且费用≤6的角色" });

        if (opt == 0)
        {
            // 将对方1张活跃咚转为休息状态
            var active = opp.CostArea.FirstOrDefault(d => d.State == DonState.Active);
            if (active != null) active.State = DonState.Rest;
        }
        else
        {
            var cands = opp.Characters.Where(c => c.IsTapped && c.Info.Cost <= 6).ToList();
            if (cands.Count == 0) return;
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择对方最多1张休息状态且费用≤6的角色KO",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }
    }
}
