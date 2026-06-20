using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-048 阿龙（角色）
/// 【咚!!×1】【攻击时】我方生命卡牌为 4 张或更多的场合，对方将其 1 张手牌放回其卡组最下方。
///
/// 实现说明：
/// - 条件：贴咚≥1 且我方生命≥4 才发动。
/// - "对方将其 1 张手牌放回卡组最下方" → 由对方决策选 1 张手牌（隐藏区，传 choiceCards），
///   用 ReturnHandToDeckBottom 放底。
/// </summary>
public class P_048_Arlong : IScriptedEffect
{
    public string CardNumber => "P-048";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        var me = ctx.State.Players[owner];
        var opp = ctx.State.Players[1 - owner];
        var self = ctx.Source;

        // 【咚!!×1】条件
        if (me.AttachedDonCount(self.Id) < 1) return;
        // 我方生命≥4
        if (me.LifeCount < 4) return;
        if (opp.Hand.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = opp.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(1 - owner, "OwnHand",
            "将你的 1 张手牌放回卡组最下方",
            opp.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (chosen.Count == 0) return;

        var card = opp.Hand.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.ReturnHandToDeckBottom(opp, card);
    }
}
