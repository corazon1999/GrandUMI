using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP01-062 克洛克达尔（领航 / 水·暗 / 王下七武海·巴洛克工作室）
/// 【咚!!×1】当我方发动事件时，我方的手牌不多于 4 张，且本回合中没有通过此领袖的效果抽取卡牌的场合，
///   可以抽取 1 张卡牌。
///
/// 实现说明：
///   - 监听"发动事件"事件（OnOppEventPlayed 对所有监听卡派发，payload owner=出牌方）；本卡仅在
///     owner == 自己（即我方发动事件）时考虑发动。
///   - 条件：我方手牌 ≤4 张，且本回合尚未通过此领袖效果抽牌（用 TurnOnceUsed 记一次性标记）。
///   - "可以抽取 1 张"用 ConfirmOptional。抽牌后写入标记，保证本回合此领袖效果只触发一次抽牌。
/// 简化点：【咚!!×1】费用门槛不在脚本判定（引擎按领袖咚条件统一处理力量/激活，抽牌收益按文本实现）。
/// </summary>
public class OP01_062_Crocodile : IScriptedEffect
{
    public string CardNumber => "OP01-062";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppEventPlayed;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅当"我方"发动事件时
        var owner = ctx.Vars.TryGetValue("owner", out var v) ? v : null;
        if (owner is not int playerWhoPlayed || playerWhoPlayed != ctx.OwnerIndex) return;

        // 条件：手牌 ≤4 张
        if (me.Hand.Count > 4) return;

        // 本回合是否已通过此领袖效果抽牌
        var key = "OP01-062-leader-draw" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "克洛克达尔（领航）：手牌≤4 且本回合未通过此领袖抽牌，抽取 1 张卡牌？");
        if (!use) return;

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        me.TurnOnceUsed.Add(key);
    }
}
