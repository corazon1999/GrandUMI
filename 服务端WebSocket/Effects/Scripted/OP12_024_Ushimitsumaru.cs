using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-024 牛鬼丸
/// 1. 持续：此角色处于活跃状态的场合，此角色不会因对方的效果而被 KO。
/// 2. 【攻击时】我方合计存在 3 张或更多被赋予中的咚!! 的场合，
///    将对方最多 1 张原本的费用不高于 6 的角色转为休息状态。
///
/// 实现说明 / 简化点：
///   - 持续防 KO 通过 PreKO 钩子实现：仅在"非战斗结算中"(s.CurrentBattle == null)
///     且自身处于活跃状态(!IsTapped)时取消本次 KO，以此近似"因对方的效果而被 KO"。
///     引擎当前不向 PreKO 传递 KO 来源(战斗/效果)，故用 CurrentBattle 是否为空来区分，
///     无法 100% 精确区分"对方效果"与"我方效果"造成的 KO（极少见）。
///   - 攻击时的"费用不高于 6"按原本费用(Info.Cost)判定。
/// </summary>
public class OP12_024_Ushimitsumaru : IScriptedEffect
{
    public string CardNumber => "OP12-024";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnAttackDeclare || t == EffectTrigger.PreKO;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.PreKO)
        {
            // 活跃状态 + 非战斗结算中（近似"因对方的效果"）→ 取消本次 KO
            if (!ctx.Source.IsTapped && ctx.State.CurrentBattle is null)
                ctx.State.MarkPreventKO(ctx.Source.Id);
            return;
        }

        // 【攻击时】
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 我方合计存在 3 张或更多被赋予中的咚!!
        int attachedDon = me.CostArea.Count(d => d.State == DonState.Attached);
        if (attachedDon < 3) return;

        var candidates = opp.Characters.Where(c => c.Info.Cost <= 6).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张原本的费用不高于 6 的角色转为休息状态",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.RestCard(target);
    }
}
