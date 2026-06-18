using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-099 卡尔加拉（角色）
/// 【我方的回合中】当生命卡牌离开场上时，抽取 1 张卡牌。
///   之后，本回合中，我方无法通过自己的效果抽取卡牌。
///
/// 实现说明 / 简化点：
///   - 用 Wave4 的 OnLifeLeaveField watcher，payload ctx.Vars["owner"]=失去生命的一方。
///     仅我方回合、且离场的是我方生命牌时抽 1 张。
///   - "之后本回合无法通过自己的效果抽牌" 是一个引擎暂无通道的持续抽牌禁令；
///     此处用 TurnOnceUsed 近似实现为"本回合此触发只抽一次"——即抽到第一张后本回合
///     不再因本卡触发抽牌，效果上等价于"抽 1 张后本回合本卡不再抽"。对其它卡的抽牌
///     禁令无法表达，但这是该限制的主要意义所在（避免连续掉生命连抽）。
/// </summary>
public class OP12_099_Kalgara : IScriptedEffect
{
    public string CardNumber => "OP12-099";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnLifeLeaveField;

    public Task Resolve(EffectContext ctx)
    {
        // 仅我方的回合
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return Task.CompletedTask;

        // 离场的须为我方生命牌
        int owner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (owner != ctx.OwnerIndex) return Task.CompletedTask;

        var me = ctx.State.Players[ctx.OwnerIndex];

        // 本回合 1 次（近似"之后本回合无法通过自己的效果抽牌"）
        var key = ctx.Source.Info.Number + "-lifedraw" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        return Task.CompletedTask;
    }
}
