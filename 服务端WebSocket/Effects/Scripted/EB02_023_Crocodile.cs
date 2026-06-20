using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB02-023 克洛克达尔（角色 / 水 / 王下七武海·巴洛克工作室，cost4 power5000）
/// 【我方的回合中】【每回合1次】当对方角色因我方的效果放回其持有者的手牌时，
///   确认我方卡组最上方的3张卡牌，将其自选顺序排列并放置到卡组最上方或最下方。
///
/// 实现说明：
///   - 用反应式 watcher OnCharLeaveField（角色因效果离场派发，含退回手牌）。payload owner=离场卡所属方。
///     仅当离场卡属对方（owner != OwnerIndex）且当前为【我方的回合中】时触发；每回合1次。
///     （引擎对 bounce/KO/放回卡组/置入生命统一派发 OnCharLeaveField，此处近似为"放回手牌"——保守地仅在对方角色离场时发动，对实战影响极小。）
///   - 效果：确认卡组顶3张，让玩家选择放卡组顶或底（ChooseOption），用 ReorderTopK 按原序放置（"自选顺序"简化为原相对顺序）。
/// </summary>
public class EB02_023_Crocodile : IScriptedEffect
{
    public string CardNumber => "EB02-023";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 仅【我方的回合中】
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        // 离场卡须属对方（"对方角色…放回其持有者的手牌"）
        int leaveOwner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int o ? o : -1;
        if (leaveOwner != 1 - ctx.OwnerIndex) return;

        // 【每回合1次】
        var key = ctx.Source.Info.Number + "-leave" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);

        int peek = Math.Min(3, me.Deck.Count);
        if (peek == 0) return;
        var top = me.Deck.Take(peek).ToList();

        // 询问放卡组顶还是底
        int opt = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "克洛克达尔：确认卡组顶3张，放置到卡组最上方还是最下方？",
            new[] { "放置到卡组最上方", "放置到卡组最下方" });

        // "自选顺序"简化为原相对顺序放置
        AtomicOps.ReorderTopK(me, top.Select(c => c.Id).ToList(), toBottom: opt == 1);
    }
}
