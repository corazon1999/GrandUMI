using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST17-005 马歇尔·D·提奇
/// 【启动主要】【每回合1次】可以将1张手牌放回卡组最上方：
/// 赋予我方1张领袖或角色最多2张休息状态的咚!!。
/// </summary>
public sealed class ST17_005_Teach : IScriptedEffect
{
    public string CardNumber => "ST17-005";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        string key = $"ST17-005-act:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key) || me.Hand.Count == 0) return;

        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "将1张手牌放回卡组最上方，赋予我方领袖或角色最多2张休息咚!!？"))
            return;

        var cost = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "选择1张手牌放回卡组最上方",
            me.Hand.Select(card => card.Id.ToString()).ToList(), 1, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = me.Hand
                    .Select(card => new { id = card.Id.ToString(), number = card.Info.Number })
                    .ToList(),
            });
        var returned = cost.Count > 0
            ? me.Hand.FirstOrDefault(card => card.Id.ToString() == cost[0])
            : null;
        if (returned is null) return;

        me.Hand.Remove(returned);
        me.Deck.Insert(0, returned);
        me.TurnOnceUsed.Add(key);

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多1张领袖或角色，赋予最多2张休息咚!!",
            targets.Select(card => card.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = targets
                    .Select(card => new { id = card.Id.ToString(), number = card.Info.Number })
                    .ToList(),
            });
        if (chosen.Count == 0) return;

        var target = targets.FirstOrDefault(card => card.Id.ToString() == chosen[0]);
        if (target is not null)
            AtomicOps.AttachDonFromCost(me, target.Id, 2, DonState.Rest);
    }
}
