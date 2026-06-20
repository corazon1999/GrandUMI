using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-013 佩鲁（角色 / 炎 / 阿拉巴斯坦王国）
/// 【咚!!×1】【攻击时】将对方最多 1 张力量不高于 4000 的角色KO。
///
/// 实现说明：
///   - 【咚!!×1】：本卡被赋予中的咚!! ≥1 才发动。
///   - "力量不高于 4000"取当前力量（含持续效果）：ctx.State.CurrentPowerOf。
/// </summary>
public class OP04_013_Pell : IScriptedEffect
{
    public string CardNumber => "OP04-013";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 【咚!!×1】
        if (me.AttachedDonCount(self.Id) < 1) return;

        int oppIdx = 1 - ctx.OwnerIndex;
        var cands = opp.Characters
            .Where(c => ctx.State.CurrentPowerOf(oppIdx, c) <= 4000)
            .ToList();
        if (cands.Count == 0) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张力量不高于 4000 的角色KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = cands.FirstOrDefault(c => c.Id.ToString() == pick[0]);
            if (tgt is not null)
                AtomicOps.KO(ctx.State, oppIdx, tgt);
        }
    }
}
