using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-046 芍药（角色）
/// 【我方的回合中】【每回合1次】当角色因我方的效果离开场上时，
///   对方持有5张或更多手牌的场合，对方将其1张手牌放回卡组最下方。
///   之后，将此角色转为休息状态。
///
/// 实现说明：
/// - 用 Wave2 反应式 watcher OnCharLeaveField 监听"角色因效果离开场上时"。
/// - 仅我方回合内有效；每回合1次。
/// - 条件：对方手牌 ≥5 时，由对方自行选 1 张放回卡组最下方（对方驱动 prompt）。
/// - 之后将本角色转为休息状态。
/// </summary>
public class OP08_046_Shakuyaku : IScriptedEffect
{
    public string CardNumber => "OP08-046";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        // 仅【我方的回合中】生效
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // 每回合1次
        var key = self.Info.Number + "-leave" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);

        // 条件：对方持有5张或更多手牌
        if (opp.Hand.Count >= 5)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = opp.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            // 由对方选择 1 张手牌放回卡组最下方
            var chosen = await ctx.Prompts.ChooseCards(1 - ctx.OwnerIndex, "OwnHand",
                "将你的1张手牌放回卡组最下方",
                opp.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
            if (chosen.Count > 0)
            {
                var card = opp.Hand.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.ReturnHandToDeckBottom(opp, card);
            }
        }

        // 之后：将此角色转为休息状态
        AtomicOps.RestCard(self);
    }
}
