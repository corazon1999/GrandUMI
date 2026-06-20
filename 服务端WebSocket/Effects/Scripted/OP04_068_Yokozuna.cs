using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-068 横纲（角色 / 暗 / 动物·七水之城）
/// 【阻挡者】（关键词，引擎自动处理）
/// 【对方攻击时】咚!!-1（可以将我方场上指定数量的咚!!放回咚!!卡组）：
///   将对方最多 1 张费用不高于 2 的角色放回其持有者的手牌。
///
/// 实现说明：
///   - 仅实现【对方攻击时】主动效果部分；【阻挡者】由引擎处理。
///   - 可选成本咚-1；将对方费用≤2 的角色 BounceToHand 退回手牌。
/// </summary>
public class OP04_068_Yokozuna : IScriptedEffect
{
    public string CardNumber => "OP04-068";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.TotalDonInCostArea < 1) return;

        var cands = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
        if (cands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "横纲【对方攻击时】：咚!!-1，将对方 1 张费用≤2 的角色退回手牌？");
        if (!use) return;

        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张费用≤2 的角色，放回其持有者手牌",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.BounceToHand(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
