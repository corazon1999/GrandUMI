using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-054 大和（角色）
/// 【登场时】我方生命卡牌不多于 3 张的场合，抽取 2 张卡牌。
///   之后，赋予我方领袖最多 1 张休息状态的咚!!。
///
/// 说明 / 简化点：
///   - 抽 2 张仅在生命 ≤3 时执行（条件部分）。
///   - “赋予领袖最多 1 张休息状态的咚” 为无条件后续效果（与抽牌条件无关），
///     从费用区取最多 1 张以休息状态赋予领袖（沿用本仓库 AttachDonFromCost(DonState.Rest) 约定）。
///   - “最多 1 张”在此实现为直接赋予 1 张（费用区不足时自动少赋予），无额外询问。
/// </summary>
public class OP13_054_Yamato : IScriptedEffect
{
    public string CardNumber => "OP13-054";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 1. 生命卡牌不多于 3 张：抽取 2 张
        if (me.LifeCount <= 3)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
        }

        // 2. 之后，赋予我方领袖最多 1 张休息状态的咚!!（无条件）
        AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);

        return Task.CompletedTask;
    }
}
