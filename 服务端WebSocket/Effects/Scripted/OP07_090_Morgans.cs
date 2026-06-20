using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-090 摩尔冈斯（角色）
/// 【登场时】对方丢弃其 1 张手牌，并公开其手牌。之后，对方抽取 1 张卡牌。
///
/// 实现说明 / 简化点：
///   - "对方丢弃其 1 张手牌"由对方自选，用 OpponentDiscardChosen（需 ctx.Engine 非空）。
///   - "公开其手牌"用 BroadcastReveal 将对方（丢弃后、抽牌前）全部手牌牌面向双方短暂展示。
///   - "之后，对方抽取 1 张卡牌"用 Draw 给对方抽 1 张。
/// </summary>
public class OP07_090_Morgans : IScriptedEffect
{
    public string CardNumber => "OP07-090";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 对方丢弃其 1 张手牌（对方自选）
        if (opp.Hand.Count > 0 && ctx.Engine != null)
        {
            await AtomicOps.OpponentDiscardChosen(ctx.Engine, 1 - ctx.OwnerIndex, 1);
        }

        // 公开对方手牌：将对方（丢弃后、抽牌前）当前全部手牌牌面向双方短暂展示
        if (opp.Hand.Count > 0)
        {
            ctx.Engine?.BroadcastReveal(1 - ctx.OwnerIndex,
                opp.Hand.Select(c => c.Info.Number).ToList());
        }

        // 之后，对方抽取 1 张卡牌
        AtomicOps.Draw(ctx.State, 1 - ctx.OwnerIndex, 1);
    }
}
