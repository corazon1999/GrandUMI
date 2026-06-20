using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-006 泽法（角色）
/// 【咚!!×1】【攻击时】本回合中，对方最多 1 张拥有属性（特）的角色力量 -5000。
///
/// 实现说明 / 简化点：
///   - 【咚!!×1】的发动条件（贴有 ≥1 咚）由引擎在调度【攻击时】效果前判定/门控，脚本只实现收益部分。
///   - "属性（特）"对应卡面 Property 字段（"斩"/"打"/"特"…），直接用 c.Info.Property == "特" 过滤。
///   - "最多 1 张"，玩家可放弃（min=0）。
/// </summary>
public class OP11_006_Zephyr : IScriptedEffect
{
    public string CardNumber => "OP11-006";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        // 【咚!!×1】：本卡需被赋予咚≥1才发动（引擎不预检攻击时咚门槛，须脚本自检）
        if (ctx.State.Players[ctx.OwnerIndex].AttachedDonCount(ctx.Source.Id) < 1) return;

        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        var cands = opp.Characters.Where(c => c.Info.Property == "特").ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张拥有属性（特）的角色，本回合中力量 -5000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, -5000);
        }
    }
}
