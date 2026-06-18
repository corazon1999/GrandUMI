using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-095 罗兹瓦德圣（3 费 0，天龙人）
/// 【登场时】可以丢弃我方的 1 张手牌：我方的角色仅为拥有《天龙人》特征的角色的场合，
///           将对方最多 2 张原本的费用不高于 3 的角色 KO。
///
/// 实现说明：
///   - 可选成本 = 丢弃我方 1 张手牌（手牌不足或玩家放弃则不发动后续效果）。
///   - 条件 = 我方场上所有角色（含此卡自身）均拥有《天龙人》特征。
///   - 效果 = 让我方玩家选择最多 2 张原本费用 ≤3 的对方角色 KO（min=0）。
/// </summary>
public class OP13_095_Rozward : IScriptedEffect
{
    public string CardNumber => "OP13-095";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = s.Players[oppIdx];

        // 可选成本：丢弃我方 1 张手牌
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "OP13-095【登场时】：丢弃我方 1 张手牌，若我方角色仅为《天龙人》，将对方最多 2 张原本费用≤3 的角色 KO？");
        if (!use) return;

        // 选择并丢弃 1 张手牌
        var handExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var discardChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张手牌",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, handExtra);
        if (discardChosen.Count == 0) return; // 未选则视为不发动
        var toDiscard = me.Hand.FirstOrDefault(c => c.Id.ToString() == discardChosen[0]);
        if (toDiscard is null) return;
        AtomicOps.DiscardHand(me, toDiscard);

        // 条件：我方场上所有角色均拥有《天龙人》特征
        if (!me.Characters.All(c => c.Info.HasKeyword("天龙人"))) return;

        // 效果：将对方最多 2 张原本费用 ≤3 的角色 KO
        var candidates = opp.Characters.Where(c => c.Info.Cost <= 3).ToList();
        if (candidates.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 2 张原本费用≤3 的角色 KO",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 2);

        foreach (var cid in chosen)
        {
            var target = candidates.FirstOrDefault(c => c.Id.ToString() == cid);
            if (target is not null) AtomicOps.KO(s, oppIdx, target);
        }
    }
}
