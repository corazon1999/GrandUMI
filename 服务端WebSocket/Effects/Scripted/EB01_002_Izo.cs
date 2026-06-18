using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-002 伊佐（角色 / 炎 / 和之国·白胡子海盗团）
/// 【登场时】赋予我方1张领袖或角色最多1张休息状态的咚!!。
/// 【对方的攻击时】【每回合1次】可以丢弃我方的1张手牌：
///   我方领袖拥有《和之国》或《白胡子海盗团》特征的场合，本回合中，对方最多1张领袖或角色力量-2000。
/// </summary>
public class EB01_002_Izo : IScriptedEffect
{
    public string CardNumber => "EB01-002";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            var donTargets = new List<CardInstance> { me.Leader };
            donTargets.AddRange(me.Characters);
            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
                "赋予我方1张领袖或角色最多1张休息状态的咚!!",
                donTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (pick.Count > 0)
            {
                var target = donTargets.First(c => c.Id.ToString() == pick[0]);
                AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
            }
            return;
        }

        // 【对方的攻击时】【每回合1次】
        var key = ctx.Source.Info.Number + "-oppatk" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 收益前提：领袖含《和之国》或《白胡子海盗团》
        bool leaderOk = me.Leader.Info.HasKeyword("和之国") || me.Leader.Info.HasKeyword("白胡子海盗团");
        if (!leaderOk) return;
        if (me.Hand.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "伊佐【对方的攻击时】：丢弃手牌1张，使对方最多1张领袖或角色本回合力量-2000？");
        if (!use) return;

        // 成本：丢弃我方1张手牌
        var discardPick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "丢弃我方1张手牌", me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (discardPick.Count == 0) return;
        var dc = me.Hand.First(c => c.Id.ToString() == discardPick[0]);
        AtomicOps.DiscardHand(me, dc);

        me.TurnOnceUsed.Add(key);

        // 效果：对方最多1张领袖或角色 -2000
        var targets = new List<CardInstance> { opp.Leader };
        targets.AddRange(opp.Characters);
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = targets.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "对方最多1张领袖或角色本回合力量-2000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, -2000);
        }
    }
}
