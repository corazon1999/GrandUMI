using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-107 史陪西中尉（角色）
/// 【我方的回合中】【每回合1次】当我方的生命卡牌加入手牌时，本回合中，此角色的力量+2000。
///
/// 实现说明 / 简化点：
///   - 用 Wave4 的 OnLifeLeaveField watcher，payload ctx.Vars["owner"]=失去生命的一方。
///     引擎对"生命牌加入手牌时"与"生命牌离场"使用同一 watcher（无独立 toHand 标志），
///     故按规范第十六节用 owner==ctx.OwnerIndex 近似"我方生命牌加入手牌时"。
///   - 仅我方回合发动；每回合 1 次用 TurnOnceUsed 控制。
///   - 收益：本回合此角色力量 +2000。
/// </summary>
public class OP05_107_LieutenantSpacey : IScriptedEffect
{
    public string CardNumber => "OP05-107";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeLeaveField;

    public Task Resolve(EffectContext ctx)
    {
        // 仅我方的回合
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;

        // 离场（加入手牌）的须为我方生命牌
        int owner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (owner != ctx.OwnerIndex) return Task.CompletedTask;

        var me = ctx.State.Players[ctx.OwnerIndex];

        // 每回合 1 次
        var key = ctx.Source.Info.Number + "-lifehand" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        // 本回合此角色力量 +2000
        AtomicOps.AddPowerThisTurn(ctx.Source, 2000);
        return Task.CompletedTask;
    }
}
