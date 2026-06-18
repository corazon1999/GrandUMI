using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP05-058 不要再践踏生命了!!!（事件）
/// 【主要】将所有费用不高于 3 的角色放回其持有者的卡组最下方。
///          之后，双方丢弃各自的手牌直到手牌变为 5 张。
/// 【触发】将所有费用不高于 2 的角色放回其持有者卡组的最下方。
///
/// 实现说明：
///   - 费用以当前费用（含持续费用修正）评估：ctx.State.CurrentCostOf(side, c)。
///   - 放回卡组最下方用 AtomicOps.ReturnFieldToDeckBottom（会先解除贴附的咚）。
///   - 【主要】节附加"双方丢弃手牌至 5 张"：各自玩家选择要丢弃的牌（先我方后对方）。
///   - 触发节（OnLifeRevealTrigger）只做费用≤2 的放回，无弃手牌部分（与文本一致）。
/// </summary>
public class OP05_058_StopTramplingLife : IScriptedEffect
{
    public string CardNumber => "OP05-058";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        int costThreshold = ctx.Trigger == EffectTrigger.OnLifeRevealTrigger ? 2 : 3;

        // ── 将双方所有当前费用 ≤ 阈值 的角色放回其持有者卡组最下方 ──
        for (int side = 0; side < 2; side++)
        {
            var ps = ctx.State.Players[side];
            // 复制一份以避免在遍历中修改集合
            var toBottom = ps.Characters
                .Where(c => ctx.State.CurrentCostOf(side, c) <= costThreshold)
                .ToList();
            foreach (var c in toBottom)
            {
                AtomicOps.ReturnFieldToDeckBottom(ctx.State, side, c);
            }
        }

        // 触发节到此结束
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger) return;

        // ── 之后：双方丢弃各自的手牌直到手牌变为 5 张（先发动方，再对方） ──
        await DiscardDownToFive(ctx, ctx.OwnerIndex);
        await DiscardDownToFive(ctx, 1 - ctx.OwnerIndex);
    }

    private static async Task DiscardDownToFive(EffectContext ctx, int playerIdx)
    {
        var p = ctx.State.Players[playerIdx];
        int over = p.Hand.Count - 5;
        if (over <= 0) return;

        var chosen = await ctx.Prompts.ChooseCards(playerIdx, "OwnHand",
            $"丢弃 {over} 张手牌（使手牌变为 5 张）",
            p.Hand.Select(c => c.Id.ToString()).ToList(), over, over);

        foreach (var id in chosen)
        {
            var card = p.Hand.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.DiscardHand(p, card);
        }
    }
}
