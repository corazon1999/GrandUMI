using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-048 堂吉诃德·多弗拉门戈（角色 / 水 3 费 4000，王下七武海/堂吉诃德海盗团）
/// 【启动主要】【每回合1次】②（可以将费用区中 2 张咚‼转为休息状态）：
///   公开我方卡组最上方的卡牌，且该卡牌为费用不高于 4 且拥有《王下七武海》特征的角色卡牌的场合，
///   可以将其以休息状态登场。之后，将剩余的卡牌放回卡组最下方。
///
/// 实现说明 / 简化点：
///   - 成本「②（横置费用区 2 张咚）」用 AtomicOps.AttachDonFromCost 无对应；横置咚成本以 RestDon 计——
///     这里通过把 2 张 Active 咚转为 Rest 实现（手动），不足 2 张活跃咚则无法发动。
///   - 「公开卡组最上方 1 张」=> 看顶 1 张；若是费用≤4 的《王下七武海》角色，可选以休息状态登场。
///   - 「之后将剩余的卡牌放回卡组最下方」：未登场的那张（公开的顶卡）放回卡组最下方。
/// </summary>
public class OP07_048_Doflamingo : IScriptedEffect
{
    public string CardNumber => "OP07-048";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 每回合 1 次
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本：横置费用区 2 张活跃咚（②）
        var activeDon = me.CostArea.Where(d => d.State == DonState.Active).ToList();
        if (activeDon.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "多弗拉门戈【启动主要】：横置 2 张咚，公开卡组顶 1 张，若为费用≤4 的《王下七武海》角色可休息登场？");
        if (!use) return;

        // 支付成本
        for (int i = 0; i < 2; i++) activeDon[i].State = DonState.Rest;
        me.TurnOnceUsed.Add(key);

        if (me.Deck.Count == 0) return;
        var top = me.Deck[0];

        bool eligible = top.Info.Kind == CardKind.Character
            && top.Info.Cost <= 4
            && top.Info.HasKeyword("王下七武海");

        if (eligible)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = new[] { new { id = top.Id.ToString(), number = top.Info.Number } }.ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DoflamingoReveal",
                "公开的卡牌为费用≤4 的《王下七武海》角色，可将其以休息状态登场（选 0 张则放回卡组底）",
                new List<string> { top.Id.ToString() }, 0, 1, extra);
            if (chosen.Count > 0)
            {
                // 从卡组取出该卡，以休息状态登场（卡组无对应免费登场原子，手动入场）
                me.Deck.Remove(top);
                if (me.Characters.Count >= 5)
                {
                    var sacrifice = me.Characters[0];
                    me.Characters.RemoveAt(0);
                    me.Trash.Add(sacrifice);
                }
                top.TurnPlayed = ctx.State.TurnCount;
                top.IsTapped = true; // 休息状态登场
                me.Characters.Add(top);
                return; // 已登场，无剩余卡需放回
            }
        }

        // 之后：将剩余的（公开的）卡牌放回卡组最下方
        me.Deck.Remove(top);
        me.Deck.Add(top);
    }
}
