using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-102 凯米（角色）
/// 【我方的回合中】【每回合1次】当对方发动事件或【触发】效果时可以发动。
///   对方生命卡牌为 2 张或更多的场合，将双方生命区最上方的各 1 张卡牌放置到废弃区。
///
/// 实现说明 / 简化点：
/// - 引擎提供 OnOppEventPlayed（当对方发动事件时）watcher；"或【触发】效果"无对应钩子，
///   故简化为仅在"对方发动事件时"触发（与主体收益一致）。
/// - 限我方回合、每回合 1 次。
/// - "双方生命区最上方"取各自 LifeArea[0]（索引 0 为顶）放入废弃区。
/// </summary>
public class OP11_102_Camie : IScriptedEffect
{
    public string CardNumber => "OP11-102";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppEventPlayed;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 限我方回合
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        // 确认出牌方确为对方
        var owner = ctx.Vars.TryGetValue("owner", out var v) ? v : null;
        if (owner is int ownerIdx && ownerIdx == ctx.OwnerIndex) return;

        // 每回合 1 次
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 条件：对方生命卡牌为 2 张或更多
        if (opp.LifeArea.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "凯米：将双方生命区最上方的各 1 张卡牌放置到废弃区？");
        if (!use) return;

        me.TurnOnceUsed.Add(key);

        // 将双方生命区最上方各 1 张放置到废弃区
        if (me.LifeArea.Count > 0)
        {
            var topMine = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            me.Trash.Add(topMine);
        }
        if (opp.LifeArea.Count > 0)
        {
            var topOpp = opp.LifeArea[0];
            opp.LifeArea.RemoveAt(0);
            opp.Trash.Add(topOpp);
        }
    }
}
