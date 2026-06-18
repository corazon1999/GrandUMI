using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP06-047 夏洛特·布玲（角色）
/// 【登场时】对方将其所有手牌放回卡组并洗牌。之后，对方抽取 5 张卡牌。
/// 把对方手牌逐张放回卡组最下方，洗混卡组，再让对方抽 5 张。
/// </summary>
public class OP06_047_CharlotteBrulee : IScriptedEffect
{
    public string CardNumber => "OP06-047";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        int oppIdx = 1 - ctx.OwnerIndex;
        var opp = ctx.State.Players[oppIdx];

        // 对方所有手牌放回卡组
        foreach (var card in opp.Hand.ToList())
        {
            AtomicOps.ReturnHandToDeckBottom(opp, card);
        }
        // 洗牌
        AtomicOps.Shuffle(ctx.State, opp.Deck);
        // 抽 5 张
        AtomicOps.Draw(ctx.State, oppIdx, 5);

        return Task.CompletedTask;
    }
}
