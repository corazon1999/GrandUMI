using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-106 宙斯（角色）
/// 【登场时】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：将对方最多 1 张费用不高于 5 的角色 KO。
///
/// 实现：可选成本（"可以…：…"）。无生命牌则无法支付成本。玩家确认后，选生命区最上方/最下方 1 张
///       加入手牌（成本），再将对方最多 1 张费用≤5 角色 KO（收益，可放弃）。
/// </summary>
public class OP11_106_Zeus : IScriptedEffect
{
    public string CardNumber => "OP11-106";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.LifeArea.Count == 0) return; // 无生命牌 → 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "宙斯【登场时】：将生命区 1 张卡牌加入手牌，以将对方 1 张费用≤5 的角色 KO？");
        if (!use) return;

        // 成本：选生命区最上方或最下方 1 张加入手牌
        int where = me.LifeArea.Count == 1
            ? 0
            : await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "将生命区哪一张加入手牌？", new[] { "最上方", "最下方" });
        var lifeCard = where == 0 ? me.LifeArea[0] : me.LifeArea[^1];
        me.LifeArea.Remove(lifeCard);
        me.Hand.Add(lifeCard);

        // 收益：将对方最多 1 张费用≤5 的角色 KO
        var cands = opp.Characters.Where(c => c.Info.Cost <= 5).ToList();
        if (cands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacterCostLe5",
            "将对方 1 张费用≤5 的角色 KO（可放弃）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count < 1) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
    }
}
