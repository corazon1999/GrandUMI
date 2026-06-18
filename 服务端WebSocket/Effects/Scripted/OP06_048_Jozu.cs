using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-048 卓夫（角色）
/// 【我方的回合中】当对方发动【阻挡者】效果或事件时，我方领袖拥有《东海》特征的场合，
///   可以将我方卡组最上方的 4 张卡牌放置到废弃区。
///
/// 实现说明：
/// - 用 Wave3 的 OnOppEventPlayed（对方发动事件时）与 Wave5 的 OnOppBlocker（对方发动【阻挡者】时）两个 watcher。
/// - 仅我方回合生效（CurrentTurnPlayer == OwnerIndex），且仅当确为对方发动（owner/blockerOwner != OwnerIndex）。
/// - 仅当我方领袖拥有《东海》特征时有效。
/// - "可以"=可选，先 ConfirmOptional。
/// </summary>
public class OP06_048_Jozu : IScriptedEffect
{
    public string CardNumber => "OP06-048";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.OnOppEventPlayed || t == EffectTrigger.OnOppBlocker;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅【我方的回合中】生效
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        // 确认确为"对方"发动事件 / 阻挡者
        if (ctx.Trigger == EffectTrigger.OnOppEventPlayed)
        {
            var owner = ctx.Vars.TryGetValue("owner", out var v) && v is int oi ? oi : -1;
            if (owner == ctx.OwnerIndex) return;
        }
        else // OnOppBlocker
        {
            var bOwner = ctx.Vars.TryGetValue("blockerOwner", out var v) && v is int bi ? bi : -1;
            if (bOwner == ctx.OwnerIndex) return;
        }

        // 我方领袖须拥有《东海》特征
        if (!me.Leader.Info.HasKeyword("东海")) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卓夫：对方发动阻挡者/事件，可以将我方卡组最上方的 4 张卡牌放置到废弃区？");
        if (!use) return;

        AtomicOps.MillTop(me, 4);
    }
}
