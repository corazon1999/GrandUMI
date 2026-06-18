using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-095 山智（角色 / 水）
/// 【对方的攻击时】【每回合1次】可以丢弃我方手牌中的1张事件：
///   本次战斗中，我方最多1张领袖或角色力量+2000。
///
/// 实现说明：
///   - 【对方的攻击时】= OnOppAttackDeclare；【每回合1次】用 TurnOnceUsed 去重。
///   - 成本：丢弃手牌中1张事件（DiscardHand）；可选发动用 ConfirmOptional。
///   - 收益：选我方1张领袖或角色，本次战斗 AddPowerThisBattle(+2000)。
/// </summary>
public class P_095_Sanji : IScriptedEffect
{
    public string CardNumber => "P-095";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var key = "P-095-counter";
        if (me.TurnOnceUsed.Contains(key)) return;

        var events = me.Hand.Where(c => c.Info.Kind == CardKind.Event).ToList();
        if (events.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "山智【对方的攻击时】：丢弃手牌中1张事件，使我方1张领袖或角色本次战斗+2000？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        // 成本：弃1张事件
        var dExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = events.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var disc = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandEvent",
            "丢弃手牌中的1张事件", events.Select(c => c.Id.ToString()).ToList(), 1, 1, dExtra);
        if (disc.Count < 1) return; // 未支付成本
        var dCard = events.First(c => c.Id.ToString() == disc[0]);
        AtomicOps.DiscardHand(me, dCard);

        // 收益：我方1张领袖或角色本次战斗+2000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择1张领袖或角色，本次战斗力量+2000（最多1张）",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var t = targets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisBattle(t, 2000);
        }
    }
}
