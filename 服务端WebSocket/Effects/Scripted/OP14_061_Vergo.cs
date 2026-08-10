using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-061 维尔高（角色 / 暗 / 班克禁区·海军·堂吉诃德海盗团）
/// 【攻击时】部分：
///   【攻击时】咚!!-1：本回合中，对方最多 1 张角色力量 -2000。
///
/// 实现说明：
///   - 第一段覆盖效果KO和非KO效果离场；支付咚!!-1后使我方《堂吉诃德海盗团》角色不离场。
///   - 触发节自带成本咚!!-1，DSL trigger 不支持成本，故用脚本：手动以 ReturnDonToDeck 支付。
///   - 成本不足（活跃咚 < 1）时无法发动；为强制效果（无“可以”），但因带成本仍询问是否支付。
/// </summary>
public class OP14_061_Vergo : IScriptedEffect
{
    public string CardNumber => "OP14-061";

    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.OnAttackDeclare or EffectTrigger.OnAllyWillBeKOd or EffectTrigger.OnAllyWillLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger != EffectTrigger.OnAttackDeclare)
        {
            bool nonKoLeave = ctx.Trigger == EffectTrigger.OnAllyWillLeaveField;
            if (!nonKoLeave &&
                (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex)) return;
            var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
            var victimOwner = ctx.Vars.TryGetValue("victimOwner", out var vo) && vo is int oi ? oi : -1;
            var victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
            if (victimOwner != ctx.OwnerIndex || victim is null ||
                !victim.Info.HasKeyword("堂吉诃德海盗团") || me.CostArea.Count == 0) return;

            if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "维尔高：支付咚!!-1，使该《堂吉诃德海盗团》角色不离场？")) return;
            if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;
            ctx.State.MarkPreventEffectLeaveBatch(ctx.OwnerIndex, victim.Id,
                card => card.Info.HasKeyword("堂吉诃德海贼团"), isKoReplacement: !nonKoLeave);
            return;
        }

        // 成本：咚!!-1，需有可返还的咚（活跃/休息/附着皆可）
        if (me.CostArea.Count < 1) return;

        var cands = opp.Characters.ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "维尔高【攻击时】：支付咚!!-1，使对方最多 1 张角色本回合力量 -2000？");
        if (!use) return;

        // 支付成本
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        // 效果：选择对方最多 1 张角色，本回合 -2000
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合力量 -2000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, -2000);
        }
    }
}
