using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-098 蒙奇·D·路飞（领航）
/// 置换：当我方《空岛》原本力量 ≥6000 的角色因对方将要离场时，
///   可将生命区最上方 1 张加入手牌，使该角色不离场。
/// 战斗/效果 KO 走 OnAllyWillBeKOd，非 KO 效果离场走 OnAllyWillLeaveField。
/// </summary>
public class OP15_098_Luffy : IScriptedEffect
{
    public string CardNumber => "OP15-098";
    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool isKoReplacement = ctx.Trigger == EffectTrigger.OnAllyWillBeKOd;
        var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;

        // 效果 KO 已经通过 OnAllyWillBeKOd 提供过一次置换机会；
        // BattleEngine 随后的通用离场守护不能再次询问、重复支付。
        if (!isKoReplacement &&
            ctx.Vars.TryGetValue("kind", out var kind) &&
            string.Equals(kind as string, "ko", StringComparison.OrdinalIgnoreCase)) return;

        if (isKoReplacement)
        {
            if (ctx.State.KOReason == "effect")
            {
                if (ctx.State.KOActingSide != 1 - ctx.OwnerIndex) return;
            }
            else
            {
                // 非效果 KO 只能来自当前对方攻击命中的我方角色；不能把无来源的 KO 误判成战斗。
                var battle = ctx.State.CurrentBattle;
                if (battle is null ||
                    battle.AttackerPlayerIndex != 1 - ctx.OwnerIndex ||
                    battle.DefenderPlayerIndex != ctx.OwnerIndex ||
                    battle.TargetIsLeader ||
                    battle.TargetCardId?.ToString() != victimId)
                    return;
            }
        }

        var victimOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int oi ? oi : -1;
        var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        if (victimOwner != ctx.OwnerIndex || victim is null ||
            !victim.Info.HasKeyword("空岛") || victim.Info.Power < 6000) return;
        if (me.LifeArea.Count == 0 || ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞：将生命区最上方1张加入手牌，使该《空岛》角色不离场？");
        if (!use) return;

        // 玩家响应期间可能发生超时、取消或状态恢复；支付前以当前权威状态重新校验。
        if (!me.Characters.Contains(victim) ||
            !victim.Info.HasKeyword("空岛") || victim.Info.Power < 6000 ||
            me.LifeArea.Count == 0 ||
            ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return;

        var top = me.LifeArea[0];
        me.LifeArea.RemoveAt(0);
        top.IsLifeFaceUp = false;
        me.Hand.Add(top);
        ctx.State.MarkPreventEffectLeaveBatch(ctx.OwnerIndex, victim.Id,
            card => card.Info.HasKeyword("空岛") && card.Info.Power >= 6000,
            isKoReplacement: isKoReplacement);
    }
}
