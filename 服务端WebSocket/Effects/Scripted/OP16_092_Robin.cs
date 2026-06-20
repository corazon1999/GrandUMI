using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-092 妮古·罗宾（角色）
/// 【登场时】可以丢弃我方手牌中 1 张费用为 8 或更高的角色卡牌：抽取 2 张卡牌。
///
/// 实现：可选成本（"可以…：…"）。手牌中无「费用≥8 的角色」候选则不发动；
///       玩家确认后丢 1 张该类手牌（向客户端下发 choiceCards 以显示卡面），再抽 2。
/// </summary>
public class OP16_092_Robin : IScriptedEffect
{
    public string CardNumber => "OP16-092";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Hand.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost >= 8).ToList();
        if (cands.Count == 0) return; // 无可丢弃的费用≥8 角色 → 不发动

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "妮古·罗宾【登场时】：丢弃 1 张费用≥8 的角色卡，抽取 2 张？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃 1 张费用≥8 的角色卡作为成本",
            cands.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (chosen.Count < 1) return;

        var card = cands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.DiscardHand(me, card);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
    }
}
