using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-012 弗兰奇（角色）
/// 【我方的回合中】【每回合1次】当对方发动事件时，本回合中，我方的所有角色力量 +2000。
///
/// 实现说明：
///   - 用 Wave3 反应式 watcher OnOppEventPlayed 监听"对方发动事件时"。
///   - 限【我方的回合中】、每回合 1 次；payload owner 为出牌方，需确为对方。
///   - 效果：我方所有角色（不含领袖）本回合力量 +2000，用 AddPowerToAllThisTurn。
/// </summary>
public class OP11_012_Franky : IScriptedEffect
{
    public string CardNumber => "OP11-012";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnOppEventPlayed;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【我方的回合中】
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;

        // 出牌方需确为对方
        int evOwner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (evOwner == ctx.OwnerIndex) return Task.CompletedTask;

        // 【每回合1次】
        var key = CardNumber + "-oppevent" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        // 本回合中，我方所有角色力量 +2000
        AtomicOps.AddPowerToAllThisTurn(ctx.State, ctx.OwnerIndex, _ => true, 2000, includeLeader: false);

        return Task.CompletedTask;
    }
}
