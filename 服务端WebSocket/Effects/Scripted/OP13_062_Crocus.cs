using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-062 克罗卡斯（5 费 6000，罗杰海盗团，暗）
/// 【登场时】我方场上存在被赋予中的咚!!的场合，从咚!!卡组中追加最多 1 张活跃状态的咚!!。
/// 【攻击时】将对方最多 1 张原本的力量不高于 3000 的角色放回其持有者的手牌。
///
/// 实现：
///   - 登场时：若我方费用区存在 ≥1 张 Attached 咚，从咚卡组追加 1 张活跃状态咚。
///   - 攻击时：从对方"原本力量(Info.Power) ≤3000"的角色中选最多 1 张回手（min=0，可不选）。
/// </summary>
public class OP13_062_Crocus : IScriptedEffect
{
    public string CardNumber => "OP13-062";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = s.Players[oppIdx];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 条件：我方场上存在被赋予中的咚!!
            bool hasAttachedDon = me.CostArea.Count(d => d.State == DonState.Attached) >= 1;
            if (!hasAttachedDon) return;
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
            return;
        }

        // OnAttackDeclare：将对方最多 1 张原本力量 ≤3000 的角色回手
        var candidates = opp.Characters.Where(c => c.Info.Power <= 3000).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张原本力量不高于 3000 的角色放回手牌",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.BounceToHand(s, oppIdx, target);
    }
}
