using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-048 笹木（角色 / 水 / 百兽海盗团）
/// 【登场时】将我方的所有手牌放回卡组，并切洗卡组。之后，抽取与放回张数相同数量的卡牌。
///
/// 实现说明：
///   - 统计当前手牌张数 n，将全部手牌放回卡组（ReturnHandToDeckBottom 逐张），Shuffle 切洗，
///     再抽取 n 张。注意本卡自身已登场在场上、不在手牌内，不受影响。
/// </summary>
public class OP04_048_Sasaki : IScriptedEffect
{
    public string CardNumber => "OP04-048";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        var hand = me.Hand.ToList();
        int n = hand.Count;
        if (n == 0) return Task.CompletedTask;

        foreach (var c in hand) AtomicOps.ReturnHandToDeckBottom(me, c);
        AtomicOps.Shuffle(ctx.State, me.Deck);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, n);
        return Task.CompletedTask;
    }
}
