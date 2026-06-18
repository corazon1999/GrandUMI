using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-119 巴索罗缪·大熊（6 费 7000，王下七武海/革命军）
///
/// 卡面效果：
///   1. 【登场时】可以丢弃我方的 1 张手牌：将我方卡组最上方的最多 1 张卡牌加入生命区最上方。
///      之后，直到下个对方的结束阶段结束时为止，此角色费用+2。
///   2. 【对方的回合中】【KO时】将我方卡组最上方的最多 1 张卡牌加入生命区最上方。
///
/// 实现说明：
/// - 登场时为可选支付（丢弃 1 张手牌）：手牌选择中选 0 张则跳过整个效果；选 1 张则继续。
/// - "将卡组最上方的最多 1 张加入生命区最上方" → AddLifeFromDeckTop(1)（卡组为空时自动不动作）。
/// - "费用+2 直到下个对方的结束阶段结束时为止" → AddCostModifier(self, +2, UntilNextOpponentEndPhase)。
/// - KO时仅在"对方的回合中"生效：以 s.CurrentTurnPlayer != OwnerIndex 判定。
/// </summary>
public class OP12_119_Kuma : IScriptedEffect
{
    public string CardNumber => "OP12-119";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            // 【对方的回合中】限定
            if (s.CurrentTurnPlayer == ctx.OwnerIndex) return;
            AtomicOps.AddLifeFromDeckTop(me, 1);
            return;
        }

        // ── 【登场时】 ──
        // 可选支付：丢弃 1 张手牌（选 0 张 = 不发动整个效果）
        if (me.Hand.Count == 0) return;

        var handExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = me.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var discardPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardOwnChosen",
            "可以丢弃 1 张手牌以发动效果（选 0 张跳过）",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 0, 1, handExtra);
        if (discardPick.Count == 0) return;

        var discardCard = me.Hand.FirstOrDefault(c => c.Id.ToString() == discardPick[0]);
        if (discardCard is null) return;
        AtomicOps.DiscardHand(me, discardCard);

        // 将卡组最上方的最多 1 张卡牌加入生命区最上方
        AtomicOps.AddLifeFromDeckTop(me, 1);

        // 之后，直到下个对方的结束阶段结束时为止，此角色费用+2
        AtomicOps.AddCostModifier(ctx.Source, 2, KeywordDuration.UntilNextOpponentEndPhase);
    }
}
