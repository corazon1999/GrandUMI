using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP14-061 维尔高（角色 / 暗 / 班克禁区·海军·堂吉诃德海盗团）
/// 仅实现【攻击时】部分：
///   【攻击时】咚!!-1：本回合中，对方最多 1 张角色力量 -2000。
///
/// 实现说明 / 简化点：
///   - 第一段“我方《堂吉诃德海盗团》角色因对方效果将要离场时，可放回 1 张咚使其不离场”
///     属于离场替代的特殊状态机制，引擎无对应通道，未实现（详见 complex 说明，此处仅做攻击效果）。
///   - 触发节自带成本咚!!-1，DSL trigger 不支持成本，故用脚本：手动以 ReturnDonToDeck 支付。
///   - 成本不足（活跃咚 < 1）时无法发动；为强制效果（无“可以”），但因带成本仍询问是否支付。
/// </summary>
public class OP14_061_Vergo : IScriptedEffect
{
    public string CardNumber => "OP14-061";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

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
