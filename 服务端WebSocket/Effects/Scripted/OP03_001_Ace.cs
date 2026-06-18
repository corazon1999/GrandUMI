using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-001 波特夹斯·D·艾斯（领袖）
/// 当此领袖进行攻击或被攻击时，可以丢弃我方手牌中任意张数的事件或舞台卡牌。
/// 每丢弃 1 张卡牌，本次战斗中，此领袖的力量 +1000。
///
/// 实现说明：
///   - 攻击时(OnAttackDeclare)与被攻击时(OnOppAttackDeclare)均触发。
///   - 候选为手牌中的事件或舞台卡；玩家可选 0..全部张数丢弃，每张 +1000(ThisBattle)。
/// </summary>
public class OP03_001_Ace : IScriptedEffect
{
    public string CardNumber => "OP03-001";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnAttackDeclare || t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var cands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Event || c.Info.Kind == CardKind.Stage).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandEventOrStage",
            "可以丢弃任意张数事件/舞台卡，每丢弃 1 张此领袖本次战斗 +1000",
            cands.Select(c => c.Id.ToString()).ToList(), 0, cands.Count, extra);
        if (chosen.Count == 0) return;

        foreach (var id in chosen)
        {
            var card = cands.First(c => c.Id.ToString() == id);
            AtomicOps.DiscardHand(me, card);
            AtomicOps.AddPowerThisBattle(ctx.Source, 1000);
        }
    }
}
