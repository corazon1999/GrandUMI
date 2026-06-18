using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-044 义远（角色）
/// 【我方的回合中】【每回合1次】当对方发动事件时，对方将其 1 张手牌放回卡组最下方。
///
/// 实现说明：
///  - 用 Wave3 的 OnOppEventPlayed watcher（对方发动事件时派发，payload owner=出牌方下标）。
///  - 限定"我方的回合中"：判 ctx.State.CurrentTurnPlayer == ctx.OwnerIndex。
///  - 确认确为对方出牌：(int)ctx.Vars["owner"] != ctx.OwnerIndex。
///  - 每回合 1 次：用 TurnOnceUsed 控制。
///  - "对方将其 1 张手牌放回卡组最下方"由对方选择：向对方发 ChooseCards（强制 1 张），
///    再 AtomicOps.ReturnHandToDeckBottom。
/// </summary>
public class OP06_044_Gion : IScriptedEffect
{
    public string CardNumber => "OP06-044";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppEventPlayed;

    public async Task Resolve(EffectContext ctx)
    {
        // 仅在我方的回合中
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        // 确认确为对方发动事件
        int owner = ctx.Vars.TryGetValue("owner", out var v) && v is int i ? i : -1;
        if (owner != 1 - ctx.OwnerIndex) return;

        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 每回合 1 次
        var key = ctx.Source.Info.Number + "-onoppevent" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        if (opp.Hand.Count == 0) return;
        me.TurnOnceUsed.Add(key);

        // 对方选择 1 张手牌放回卡组最下方
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = opp.Hand.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(1 - ctx.OwnerIndex, "OwnHand",
            "将你的 1 张手牌放回卡组最下方",
            opp.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = opp.Hand.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.ReturnHandToDeckBottom(opp, picked);
        }
    }
}
