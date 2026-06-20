using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-095 肥皂羊（事件 / 地 / CP9）
/// 【主要】本回合中，对方最多 2 张角色费用-2。
/// 【触发】对方丢弃其 1 张手牌。
///
/// 实现说明：
///   - 主要段(EventMain)：让玩家选对方最多 2 张角色，逐张施加本回合费用 -2（AddCostModifier）。
///   - 触发段(OnLifeRevealTrigger)：对方丢弃 1 张手牌，用 OpponentDiscardChosen（需 ctx.Engine）。
/// </summary>
public class OP03_095_SoapSheep : IScriptedEffect
{
    public string CardNumber => "OP03-095";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            // 【触发】对方丢弃其 1 张手牌
            if (ctx.Engine is not null)
                await AtomicOps.OpponentDiscardChosen(ctx.Engine, 1 - ctx.OwnerIndex, 1);
            return;
        }

        // 【主要】对方最多 2 张角色费用-2（本回合）
        var cands = opp.Characters.ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 2 张角色，各费用-2（本回合）",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 2, extra);

        foreach (var id in chosen)
        {
            var tgt = cands.First(c => c.Id.ToString() == id);
            AtomicOps.AddCostModifier(tgt, -2, KeywordDuration.ThisTurn);
        }
    }
}
