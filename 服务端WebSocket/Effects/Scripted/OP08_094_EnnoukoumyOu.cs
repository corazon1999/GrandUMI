using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-094 炎皇（事件 / 地 / 2 费 / 百兽海盗团）
/// 【主要】/【反击】可以将我方废弃区中的 3 张卡牌自选顺序放回卡组最下方：
///   将对方最多 1 张费用不高于 2 的角色 KO。
///
/// 说明 / 简化点：
///   - 成本为可选：需废弃区 ≥3 张卡牌才可发动；选 3 张按选择顺序放回卡组最下方。
///   - 【主要】走 EventMain，【反击】走 EventCounter，两时机逻辑一致。
/// </summary>
public class OP08_094_EnnoukoumyOu : IScriptedEffect
{
    public string CardNumber => "OP08-094";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本候选：废弃区任意卡牌，需 ≥3 张
        if (me.Trash.Count < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "炎皇：将废弃区 3 张卡牌放回卡组最下方，将对方最多 1 张费用≤2 的角色 KO？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Trash.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var picks = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "Trash",
            "选择废弃区 3 张卡牌放回卡组最下方（自选顺序）",
            me.Trash.Select(c => c.Id.ToString()).ToList(), 3, 3, extra);
        if (picks.Count < 3) return;

        foreach (var id in picks)
        {
            var card = me.Trash.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        // 效果：将对方最多 1 张费用≤2 的角色 KO
        var cands = opp.Characters.Where(c => ctx.State.CurrentCostOf(c) <= 2).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张费用≤2 的角色 KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
