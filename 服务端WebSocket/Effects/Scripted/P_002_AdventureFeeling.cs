using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-002 我有一种要历险的感觉!!（事件 / 炎 / 草帽一伙，cost1）
/// 【主要】将我方的所有手牌放回卡组，并切洗卡组。之后，抽取与放回张数相同的卡牌。
/// 【触发】发动此卡牌的【主要】效果。
///
/// 实现说明：
///   - 事件发动时本卡已离开手牌，故"所有手牌"= 剩余手牌。先统计张数 n，逐张放回卡组最下方，
///     再切洗卡组，最后抽 n 张（净手牌数不变，等价于换牌）。
///   - 生命【触发】走同一 EventMain 路径（HandlesTrigger 含 OnLifeRevealTrigger），效果一致。
/// </summary>
public class P_002_AdventureFeeling : IScriptedEffect
{
    public string CardNumber => "P-002";

    public bool HandlesTrigger(EffectTrigger t) =>
        t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 将我方所有手牌放回卡组最下方，记录放回张数
        var handCards = me.Hand.ToList();
        int n = handCards.Count;
        foreach (var c in handCards)
            AtomicOps.ReturnHandToDeckBottom(me, c);

        // 切洗卡组
        if (ctx.Engine is not null)
            ctx.Engine.ShuffleDeck(me, ctx.OwnerIndex, "P-002_adventure_feeling");
        else
            AtomicOps.Shuffle(ctx.State, me.Deck);

        // 抽取与放回张数相同的卡牌
        if (n > 0)
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, n);

        return Task.CompletedTask;
    }
}
