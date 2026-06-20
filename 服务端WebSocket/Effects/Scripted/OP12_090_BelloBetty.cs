using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-090 贝洛·贝蒂（3 费 4000，革命军）
/// 【攻击时】可以将我方卡组最上方的 2 张卡牌放置到废弃区：本回合中，对方最多 1 张角色费用 -2。
///
/// 说明：
///   - 整个效果为可选（"可以…"）。可选额外成本：自我方卡组顶 2 张入废弃区（牌库不足 2 张则无法支付）。
///   - 之后选对方最多 1 张角色，本回合费用 -2（ThisTurn）。
/// </summary>
public class OP12_090_BelloBetty : IScriptedEffect
{
    public string CardNumber => "OP12-090";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 可选额外成本：卡组顶 2 张入废弃区
        if (me.Deck.Count < 2) return;
        if (opp.Characters.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "贝蒂【攻击时】：将我方卡组最上方 2 张放置到废弃区，使对方最多 1 张角色本回合费用 -2？");
        if (!use) return;

        // 支付成本
        AtomicOps.MillTop(me, 2);

        // 效果：对方最多 1 张角色费用 -2（本回合）
        var candidates = opp.Characters.ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张角色，本回合费用 -2",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count == 0) return;

        var target = candidates.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is null) return;

        AtomicOps.AddCostModifier(target, -2, KeywordDuration.ThisTurn);
    }
}
