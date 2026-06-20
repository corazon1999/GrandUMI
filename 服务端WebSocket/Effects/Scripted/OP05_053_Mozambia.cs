using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-053 莫桑比亚（角色）
/// 【我方的回合中】【每回合1次】当我方在抽卡阶段以外的时候抽取卡牌时，本回合中，此角色的力量+2000。
///
/// 实现说明：
///   - 使用反应式 watcher 触发 OnDrawCard（效果内抽牌=抽卡阶段以外抽牌时派发，payload: count/player）。
///   - 仅【我方的回合中】且抽牌方为我方时生效；每回合1次。
///   - 收益：本回合中此角色力量 +2000（AddPowerThisTurn）。
/// </summary>
public class OP05_053_Mozambia : IScriptedEffect
{
    public string CardNumber => "OP05-053";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnDrawCard;

    public Task Resolve(EffectContext ctx)
    {
        // 仅【我方的回合中】生效
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;

        // 仅当抽牌方为我方时生效
        if (ctx.Vars.TryGetValue("player", out var pv) && pv is int p && p != ctx.OwnerIndex)
            return Task.CompletedTask;

        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【每回合1次】
        var key = ctx.Source.Info.Number + "-act" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        // 本回合中，此角色力量 +2000
        AtomicOps.AddPowerThisTurn(ctx.Source, 2000);
        return Task.CompletedTask;
    }
}
